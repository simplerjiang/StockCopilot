using System.Diagnostics;
using System.Text.Json;

namespace SimplerJiangAiAgent.Api.Infrastructure;

public interface IFinancialWorkerSupervisor
{
    FinancialWorkerStatus GetStatus();
    Task StartWorkerAsync(CancellationToken ct);
    Task StopWorkerAsync(CancellationToken ct);
    Task RestartWorkerAsync(CancellationToken ct);
}

public record FinancialWorkerStatus(
    string State,           // "running", "stopped", "starting", "error"
    bool IsHealthy,
    DateTime? LastHeartbeat,
    int? ProcessId,
    string? LastError,
    FinancialWorkerHealthDto? LastHealthResponse,
    DateTime? WorkerStartedAt,
    int AutoRestartCount = 0,
    DateTime? LastAutoRestart = null
);

public record FinancialWorkerHealthDto(
    string? Service,
    string? Status,
    string? DataRoot,
    string? DbPath,
    DateTime? Timestamp,
    string? CurrentActivity,
    object? LastCollectionResult
);

public sealed class FinancialWorkerSupervisorService : BackgroundService, IFinancialWorkerSupervisor
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FinancialWorkerSupervisorService> _logger;

    private string _state = "stopped";
    private bool _isHealthy;
    private DateTime? _lastHeartbeat;
    private int? _processId;
    private string? _lastError;
    private FinancialWorkerHealthDto? _lastHealthResponse;
    private Process? _workerProcess;
    private DateTime? _workerStartedAt;
    private readonly object _lock = new();

    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(30);

    private int _consecutiveFailures;
    private const int MaxConsecutiveFailures = 3; // 30 秒无响应后重启

    private int _autoRestartCount;
    private DateTime? _lastAutoRestart;

    public FinancialWorkerSupervisorService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<FinancialWorkerSupervisorService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(5000, stoppingToken); // 等主 API 完全启动

        await TryAutoStartAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await PerformHeartbeatAsync(stoppingToken);
            try
            {
                await Task.Delay(HeartbeatInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        await StopWorkerInternalAsync();
    }

    private async Task PerformHeartbeatAsync(CancellationToken ct)
    {
        // 用户主动停止后不再做心跳检查，避免产生误导性错误
        lock (_lock)
        {
            if (_state == "stopped") return;
        }

        // Proactive process exit detection — immediate restart without waiting for heartbeat threshold
        bool processExited = false;
        lock (_lock)
        {
            if (_workerProcess != null && _workerProcess.HasExited)
            {
                _logger.LogWarning("Worker process (PID {Pid}) has exited with code {ExitCode}. Scheduling immediate restart.",
                    _workerProcess.Id, _workerProcess.ExitCode);
                _state = "error";
                _isHealthy = false;
                _lastError = SanitizeError($"Process exited with code {_workerProcess.ExitCode}");
                _workerProcess = null;
                _processId = null;
                _consecutiveFailures = 0;
                processExited = true;
            }
        }

        if (processExited)
        {
            _ = Task.Run(async () =>
            {
                try { await TryAutoStartAsync(CancellationToken.None); }
                catch (Exception ex) { _logger.LogError(ex, "Auto-restart after process exit failed"); }
            });
            return;
        }

        var baseUrl = ResolveBaseUrl();
        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            var response = await client.GetAsync($"{baseUrl}/health", ct);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(ct);
                var healthDto = JsonSerializer.Deserialize<FinancialWorkerHealthDto>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                lock (_lock)
                {
                    if (_state != "running")
                        _workerStartedAt = DateTime.UtcNow;
                    _state = "running";
                    _isHealthy = true;
                    _lastHeartbeat = DateTime.UtcNow;
                    _lastHealthResponse = healthDto;
                    _consecutiveFailures = 0;
                    _autoRestartCount = 0;
                    _lastError = null;

                    if (_workerProcess is null || _workerProcess.HasExited)
                    {
                        TryFindWorkerProcess();
                    }
                    if (_workerProcess is not null && !_workerProcess.HasExited)
                    {
                        _processId = _workerProcess.Id;
                    }
                }
            }
            else
            {
                HandleHeartbeatFailure($"HTTP {response.StatusCode}");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            if (ct.IsCancellationRequested) return;
            HandleHeartbeatFailure(ex.Message);
        }
    }

    private void HandleHeartbeatFailure(string error)
    {
        bool shouldRestart;
        lock (_lock)
        {
            _consecutiveFailures++;
            _isHealthy = false;
            _lastError = SanitizeError(error);

            if (_consecutiveFailures >= MaxConsecutiveFailures)
            {
                _state = "error";
                _logger.LogWarning("FinancialWorker heartbeat failed {Count} times, scheduling auto-restart", _consecutiveFailures);
                shouldRestart = true;
            }
            else
            {
                _state = "stopped";
                shouldRestart = false;
            }
        }

        if (shouldRestart)
        {
            lock (_lock) { _consecutiveFailures = 0; }
            _ = Task.Run(async () =>
            {
                try { await TryAutoStartAsync(CancellationToken.None); }
                catch (Exception ex) { _logger.LogError(ex, "Auto-restart failed"); }
            });
        }
    }

    private async Task TryAutoStartAsync(CancellationToken ct)
    {
        if (await CheckHealthAsync(ct))
        {
            lock (_lock) { if (_state != "running") _workerStartedAt = DateTime.UtcNow; _state = "running"; }
            _logger.LogInformation("FinancialWorker already running");
            return;
        }

        lock (_lock)
        {
            _autoRestartCount++;
            _lastAutoRestart = DateTime.UtcNow;

            if (_autoRestartCount >= 3 && _lastAutoRestart.HasValue
                && (DateTime.UtcNow - _lastAutoRestart.Value).TotalMinutes < 5)
            {
                _logger.LogError("Worker has been restarted {Count} times in 5 minutes. Possible crash loop.", _autoRestartCount);
            }
        }

        await StartWorkerInternalAsync(ct);
    }

    private async Task StartWorkerInternalAsync(CancellationToken ct)
    {
        lock (_lock)
        {
            _state = "starting";
            _lastError = null;
        }

        try
        {
            var exePath = ResolveWorkerExePath();
            if (exePath is null)
            {
                lock (_lock)
                {
                    _state = "error";
                    _lastError = SanitizeError("Cannot find FinancialWorker executable");
                }
                _logger.LogError("FinancialWorker executable not found");
                return;
            }

            _logger.LogInformation("Starting FinancialWorker: {Path}", exePath);

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = Path.GetDirectoryName(exePath)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            // source 模式（.dll）用 dotnet 运行
            if (exePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                psi.FileName = "dotnet";
                psi.Arguments = $"\"{exePath}\"";
            }

            var process = Process.Start(psi);
            if (process is null)
            {
                lock (_lock)
                {
                    _state = "error";
                    _lastError = SanitizeError("Failed to start process");
                }
                return;
            }

            lock (_lock)
            {
                _workerProcess = process;
                _processId = process.Id;
            }

            // 等待 Worker 健康
            var deadline = DateTime.UtcNow.Add(StartupTimeout);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(2000, ct);
                if (await CheckHealthAsync(ct))
                {
                    lock (_lock)
                    {
                        if (_state != "running")
                            _workerStartedAt = DateTime.UtcNow;
                        _state = "running";
                        _isHealthy = true;
                        _lastHeartbeat = DateTime.UtcNow;
                    }
                    _logger.LogInformation("FinancialWorker started successfully, PID={PID}", process.Id);
                    return;
                }
            }

            lock (_lock)
            {
                _state = "error";
                _lastError = SanitizeError("Worker started but health check timed out");
            }
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                _state = "error";
                _lastError = SanitizeError(ex.Message);
            }
            _logger.LogError(ex, "Failed to start FinancialWorker");
        }
    }

    private async Task StopWorkerInternalAsync()
    {
        Process? proc;
        lock (_lock)
        {
            proc = _workerProcess;
        }

        if (proc is not null && !proc.HasExited)
        {
            try
            {
                proc.Kill(true);
                await proc.WaitForExitAsync();
                _logger.LogInformation("FinancialWorker stopped");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error stopping FinancialWorker");
            }
        }

        TryKillByProcessName();

        lock (_lock)
        {
            _state = "stopped";
            _isHealthy = false;
            _processId = null;
            _workerProcess = null;
            _lastError = null;
            _lastHealthResponse = null;
            _workerStartedAt = null;
        }
    }

    // === IFinancialWorkerSupervisor ===

    public FinancialWorkerStatus GetStatus()
    {
        lock (_lock)
        {
            return new FinancialWorkerStatus(
                _state, _isHealthy, _lastHeartbeat, _processId, SanitizeError(_lastError), _lastHealthResponse, _workerStartedAt,
                _autoRestartCount, _lastAutoRestart
            );
        }
    }

    private static string? SanitizeError(string? message)
        => ErrorSanitizer.SanitizeErrorMessage(message);

    public async Task StartWorkerAsync(CancellationToken ct) => await StartWorkerInternalAsync(ct);

    public async Task StopWorkerAsync(CancellationToken ct) => await StopWorkerInternalAsync();

    public async Task RestartWorkerAsync(CancellationToken ct)
    {
        await StopWorkerInternalAsync();
        await Task.Delay(2000, ct);
        await StartWorkerInternalAsync(ct);
    }

    // === Helpers ===

    private string ResolveBaseUrl()
    {
        var configured = _configuration["FinancialWorker:BaseUrl"]?.Trim();
        if (Uri.TryCreate(configured, UriKind.Absolute, out var uri))
            return uri.ToString().TrimEnd('/');
        return "http://localhost:5120";
    }

    private string? ResolveWorkerExePath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "FinancialWorker", "SimplerJiangAiAgent.FinancialWorker.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "FinancialWorker", "SimplerJiangAiAgent.FinancialWorker.exe"),
            // source 模式：查找编译输出
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                "SimplerJiangAiAgent.FinancialWorker", "bin", "Debug", "net8.0", "SimplerJiangAiAgent.FinancialWorker.dll"),
        };

        foreach (var path in candidates)
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
                return fullPath;
        }

        var configuredPath = _configuration["FinancialWorker:ExePath"]?.Trim();
        if (!string.IsNullOrEmpty(configuredPath) && File.Exists(configuredPath))
            return configuredPath;

        return null;
    }

    private async Task<bool> CheckHealthAsync(CancellationToken ct)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(3);
            var response = await client.GetAsync($"{ResolveBaseUrl()}/health", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private void TryFindWorkerProcess()
    {
        try
        {
            var procs = Process.GetProcessesByName("SimplerJiangAiAgent.FinancialWorker");
            if (procs.Length > 0)
            {
                _workerProcess = procs[0];
            }
        }
        catch { /* ignore */ }
    }

    private void TryKillByProcessName()
    {
        try
        {
            foreach (var proc in Process.GetProcessesByName("SimplerJiangAiAgent.FinancialWorker"))
            {
                proc.Kill(true);
            }
        }
        catch { /* ignore */ }
    }
}
