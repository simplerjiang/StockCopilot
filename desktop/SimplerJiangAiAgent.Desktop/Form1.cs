using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace SimplerJiangAiAgent.Desktop;

public partial class Form1 : Form
{
    private const int PreferredBackendPort = 5119;
    private const int PreferredFinancialWorkerPort = 5120;
    private const int BackendHealthFailureThreshold = 3;
    private static readonly TimeSpan BackendStartupTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan FinancialWorkerStartupTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan BackendHealthProbeInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan BackendHealthProbeTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan BackendHealthRecoveryGracePeriod = TimeSpan.FromSeconds(20);
    private static readonly HttpClient HealthClient = new() { Timeout = BackendHealthProbeTimeout };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly WebView2 _webView;
    private readonly System.Windows.Forms.Timer _backendHealthTimer;
    private readonly object _backendLogSync = new();
    private static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SimplerJiangAiAgent", "desktop-settings.json");
    private Process? _backendProcess;
    private Process? _financialWorkerProcess;
    private BackendLaunchCommand? _backendLaunchCommand;
    private BackendLaunchCommand? _financialWorkerLaunchCommand;
    private string? _backendBaseUrl;
    private string? _backendDataRoot;
    private string? _logDirectory;
    private string? _financialWorkerBaseUrl;
    private bool _ownsBackendProcess;
    private bool _backendReady;
    private bool _isRecoveringBackend;
    private bool _isCheckingBackendHealth;
    private bool _isClosing;
    private int _backendConsecutiveHealthFailures;
    private DateTime _lastBackendHealthyAtUtc = DateTime.MinValue;
    private DateTime _lastBackendLaunchAtUtc = DateTime.MinValue;
#if DEBUG
    private readonly TextBox _debugTextBox;
    private readonly Button _debugButton;
    private bool _debugVisible;
#endif

    public Form1()
    {
        InitializeComponent();

        Text = $"SimplerJiang AI Agent v{AppUpdateService.CurrentVersionLabel}";
        WindowState = FormWindowState.Maximized;

        _webView = new WebView2
        {
            Dock = DockStyle.Fill
        };
        _backendHealthTimer = new System.Windows.Forms.Timer
        {
            Interval = (int)BackendHealthProbeInterval.TotalMilliseconds
        };
        _backendHealthTimer.Tick += OnBackendHealthTimerTickAsync;

        Controls.Add(_webView);
#if DEBUG
        _debugTextBox = new TextBox
        {
            Dock = DockStyle.Bottom,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Height = 160,
            Visible = false
        };

        _debugButton = new Button
        {
            Text = "开发模式",
            AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Top = 10,
            Left = ClientSize.Width - 110
        };

        _debugButton.Click += (_, _) => ToggleDebugPanel();
        Controls.Add(_debugTextBox);
        Controls.Add(_debugButton);

        Resize += (_, _) =>
        {
            _debugButton.Left = ClientSize.Width - _debugButton.Width - 12;
        };
#endif
        Load += OnLoadAsync;
        FormClosed += OnFormClosed;
    }

    private async void OnLoadAsync(object? sender, EventArgs e)
    {
        try
        {
            var backendBaseUrl = await EnsureBackendAsync();
            var startupUrl = await GetStartupUrlAsync(backendBaseUrl);
            await _webView.EnsureCoreWebView2Async();
            _webView.CoreWebView2.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess)
                {
                    return;
                }

                AppendDebug($"页面加载失败: {args.WebErrorStatus}");
                if (_ownsBackendProcess && !_isClosing)
                {
                    _ = HandleNavigationFailureAsync(args.WebErrorStatus);
                }
            };
#if DEBUG
            _webView.CoreWebView2.ProcessFailed += (_, args) => AppendDebug($"WebView2 进程异常: {args.Reason}");

            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                AppendDebug($"未处理异常: {args.ExceptionObject}");
            };

            Application.ThreadException += (_, args) =>
            {
                AppendDebug($"线程异常: {args.Exception}");
            };
#endif
            _webView.CoreWebView2.Navigate(startupUrl);
            _ = CheckForUpdatesAsync();
        }
        catch (Exception ex)
        {
            AppendDebug($"应用启动失败: {ex}");
            MessageBox.Show(this, ex.Message, "SimplerJiang AI Agent", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    private async Task<string> EnsureBackendAsync()
    {
        var existingUrl = $"http://localhost:{PreferredBackendPort}";
        _backendBaseUrl = existingUrl;
        _backendDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SimplerJiangAiAgent");
        Directory.CreateDirectory(_backendDataRoot);
        _logDirectory = ResolveLogDirectory(_backendDataRoot);
        var defaultLogDir = Path.Combine(_backendDataRoot, "logs");
        MigrateLogsIfNeeded(defaultLogDir, _logDirectory);

        if (await IsHealthyAsync(existingUrl))
        {
            _ownsBackendProcess = false;
            _backendReady = true;
            _lastBackendHealthyAtUtc = DateTime.UtcNow;
            await EnsureFinancialWorkerAsync(_backendDataRoot);
            AppendDebug($"复用已运行后端: {existingUrl}");
            return existingUrl;
        }

        _backendLaunchCommand = FindPackagedBackendLaunchCommand();
        if (_backendLaunchCommand is null)
        {
            throw new InvalidOperationException("未找到可启动的后端程序。请使用打包后的发布目录运行桌面程序，或先手工启动本地后端。");
        }

        await EnsureFinancialWorkerAsync(_backendDataRoot);
        await StartManagedBackendAsync(existingUrl, _backendDataRoot);
        StartBackendHealthMonitoring();
        return existingUrl;
    }

    private void OnFormClosed(object? sender, FormClosedEventArgs e)
    {
        _isClosing = true;
        _backendHealthTimer.Stop();
        TryGracefulWorkerShutdown();
        StopOwnedFinancialWorkerProcess();
        StopOwnedBackendProcess();
    }

    private void TryGracefulWorkerShutdown()
    {
        if (string.IsNullOrWhiteSpace(_backendBaseUrl)) return;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            http.PostAsync($"{_backendBaseUrl}/api/stocks/worker/stop", null).Wait(3000);
        }
        catch { /* Best effort - will force kill anyway */ }
    }

    private static BackendLaunchCommand? FindPackagedBackendLaunchCommand()
    {
        var backendDir = Path.Combine(AppContext.BaseDirectory, "Backend");
        var backendExePath = Path.Combine(backendDir, "SimplerJiangAiAgent.Api.exe");
        if (File.Exists(backendExePath))
        {
            return new BackendLaunchCommand(backendExePath, string.Empty, backendDir);
        }

        var backendDllPath = Path.Combine(backendDir, "SimplerJiangAiAgent.Api.dll");
        if (File.Exists(backendDllPath))
        {
            return new BackendLaunchCommand("dotnet", $"\"{backendDllPath}\"", backendDir);
        }

        return null;
    }

    private static BackendLaunchCommand? FindPackagedFinancialWorkerLaunchCommand()
    {
        var workerDir = Path.Combine(AppContext.BaseDirectory, "FinancialWorker");
        var workerExePath = Path.Combine(workerDir, "SimplerJiangAiAgent.FinancialWorker.exe");
        if (File.Exists(workerExePath))
        {
            return new BackendLaunchCommand(workerExePath, string.Empty, workerDir);
        }

        var workerDllPath = Path.Combine(workerDir, "SimplerJiangAiAgent.FinancialWorker.dll");
        if (File.Exists(workerDllPath))
        {
            return new BackendLaunchCommand("dotnet", $"\"{workerDllPath}\"", workerDir);
        }

        return null;
    }

    private static async Task<bool> WaitForHealthyAsync(string baseUrl, Process process, TimeSpan timeout, string healthPath = "/api/health")
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                return false;
            }

            if (await IsHealthyAsync(baseUrl, healthPath))
            {
                return true;
            }

            await Task.Delay(500);
        }

        return false;
    }

    private async Task EnsureFinancialWorkerAsync(string dataRoot)
    {
        var existingUrl = $"http://localhost:{PreferredFinancialWorkerPort}";
        _financialWorkerBaseUrl = existingUrl;

        if (await IsHealthyAsync(existingUrl, "/health"))
        {
            AppendDebug($"复用已运行财务 Worker: {existingUrl}");
            return;
        }

        _financialWorkerLaunchCommand = FindPackagedFinancialWorkerLaunchCommand();
        if (_financialWorkerLaunchCommand is null)
        {
            AppendDebug("未找到打包后的 FinancialWorker，财务采集功能将依赖外部已启动的 Worker。");
            return;
        }

        try
        {
            await StartManagedFinancialWorkerAsync(existingUrl, dataRoot);
        }
        catch (Exception ex)
        {
            AppendDebug($"FinancialWorker 启动失败: {ex.Message}");
            StopOwnedFinancialWorkerProcess();
        }
    }

    private async void OnBackendHealthTimerTickAsync(object? sender, EventArgs e)
    {
        if (_isClosing || _isRecoveringBackend || _isCheckingBackendHealth || !_ownsBackendProcess || !_backendReady || string.IsNullOrWhiteSpace(_backendBaseUrl))
        {
            return;
        }

        _isCheckingBackendHealth = true;
        try
        {
            if (await IsHealthyAsync(_backendBaseUrl))
            {
                _backendConsecutiveHealthFailures = 0;
                _lastBackendHealthyAtUtc = DateTime.UtcNow;
                return;
            }

            _backendConsecutiveHealthFailures += 1;
            var unhealthySince = _lastBackendHealthyAtUtc == DateTime.MinValue
                ? _lastBackendLaunchAtUtc
                : _lastBackendHealthyAtUtc;
            var unhealthyDuration = unhealthySince == DateTime.MinValue
                ? TimeSpan.Zero
                : DateTime.UtcNow - unhealthySince;

            AppendDebug($"后端健康检查失败，第 {_backendConsecutiveHealthFailures} 次。地址: {_backendBaseUrl}，持续失联约 {Math.Max(0, unhealthyDuration.TotalSeconds):0.#} 秒。");
            if (_backendConsecutiveHealthFailures < BackendHealthFailureThreshold)
            {
                return;
            }

            if (unhealthyDuration < BackendHealthRecoveryGracePeriod)
            {
                AppendDebug($"后端仍在短时抖动观察窗口内（<{BackendHealthRecoveryGracePeriod.TotalSeconds:0} 秒），本次不触发自动恢复。");
                return;
            }

            await RecoverBackendAsync("健康检查持续失败");
        }
        finally
        {
            _isCheckingBackendHealth = false;
        }
    }

    private async Task RecoverBackendAsync(string reason)
    {
        if (_isClosing || _isRecoveringBackend || !_ownsBackendProcess || string.IsNullOrWhiteSpace(_backendBaseUrl) || string.IsNullOrWhiteSpace(_backendDataRoot))
        {
            return;
        }

        _isRecoveringBackend = true;
        _backendReady = false;
        _backendHealthTimer.Stop();

        try
        {
            AppendDebug($"检测到后端失联，准备自动恢复。原因: {reason}");
            StopOwnedBackendProcess();
            await StartManagedBackendAsync(_backendBaseUrl, _backendDataRoot);
            _backendConsecutiveHealthFailures = 0;
            AppendDebug("后端自动恢复完成，准备刷新页面。");
            ReloadFrontend();
        }
        catch (Exception ex)
        {
            AppendDebug($"后端自动恢复失败: {ex.Message}");
        }
        finally
        {
            _isRecoveringBackend = false;
            if (!_isClosing && _ownsBackendProcess)
            {
                _backendHealthTimer.Start();
            }
        }
    }

    private async Task StartManagedBackendAsync(string baseUrl, string dataRoot)
    {
        if (_backendLaunchCommand is null)
        {
            throw new InvalidOperationException("未找到后端启动命令。请检查打包后的 Backend 目录。");
        }

        var process = CreateBackendProcess(_backendLaunchCommand, baseUrl, dataRoot, _logDirectory);
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("后端启动失败，未能创建后端进程。");
        }

        _backendProcess = process;
        _ownsBackendProcess = true;
        _backendConsecutiveHealthFailures = 0;
        _lastBackendLaunchAtUtc = DateTime.UtcNow;
        _lastBackendHealthyAtUtc = DateTime.MinValue;
        AttachBackendProcessDiagnostics(process, dataRoot);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        AppendDebug($"已启动本地后端进程: {process.Id}, 地址: {baseUrl}");
        if (!await WaitForHealthyAsync(baseUrl, process, BackendStartupTimeout))
        {
            var exitCode = TryGetProcessExitCode(process);
            StopOwnedBackendProcess();
            throw new InvalidOperationException($"后端启动超时，请检查 {Path.Combine(dataRoot, "logs")}。{(exitCode is null ? string.Empty : $" 进程退出码: {exitCode}.")}".Trim());
        }

        _backendReady = true;
        _lastBackendHealthyAtUtc = DateTime.UtcNow;
    }

    private async Task StartManagedFinancialWorkerAsync(string baseUrl, string dataRoot)
    {
        if (_financialWorkerLaunchCommand is null)
        {
            throw new InvalidOperationException("未找到 FinancialWorker 启动命令。请检查打包后的 FinancialWorker 目录。");
        }

        var process = CreateBackendProcess(_financialWorkerLaunchCommand, baseUrl, dataRoot, _logDirectory);
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("FinancialWorker 启动失败，未能创建进程。");
        }

        _financialWorkerProcess = process;
        AttachFinancialWorkerProcessDiagnostics(process, dataRoot);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        AppendDebug($"已启动财务 Worker 进程: {process.Id}, 地址: {baseUrl}");
        if (!await WaitForHealthyAsync(baseUrl, process, FinancialWorkerStartupTimeout, "/health"))
        {
            var exitCode = TryGetProcessExitCode(process);
            StopOwnedFinancialWorkerProcess();
            throw new InvalidOperationException($"FinancialWorker 启动超时，请检查 {Path.Combine(dataRoot, "logs")}。{(exitCode is null ? string.Empty : $" 进程退出码: {exitCode}.")}".Trim());
        }
    }

    private async Task HandleNavigationFailureAsync(CoreWebView2WebErrorStatus webErrorStatus)
    {
        if (_isClosing || !_ownsBackendProcess || string.IsNullOrWhiteSpace(_backendBaseUrl))
        {
            return;
        }

        if (await IsHealthyAsync(_backendBaseUrl))
        {
            AppendDebug($"页面导航失败，但后端健康检查仍通过，本次不触发自动恢复。原因: {webErrorStatus}");
            return;
        }

        await RecoverBackendAsync($"页面导航失败: {webErrorStatus}");
    }

    private static Process CreateBackendProcess(BackendLaunchCommand launchCommand, string baseUrl, string dataRoot, string? logRoot = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = launchCommand.FileName,
            Arguments = launchCommand.Arguments,
            WorkingDirectory = launchCommand.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        startInfo.Environment["DOTNET_ENVIRONMENT"] = "Production";
        startInfo.Environment["ASPNETCORE_URLS"] = baseUrl;
        startInfo.Environment["SJAI_DATA_ROOT"] = dataRoot;
        if (!string.IsNullOrWhiteSpace(logRoot))
        {
            startInfo.Environment["SJAI_LOG_ROOT"] = logRoot;
        }

        return new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
    }

    private void AttachBackendProcessDiagnostics(Process process, string dataRoot)
    {
        var logDirectory = _logDirectory ?? Path.Combine(dataRoot, "logs");
        Directory.CreateDirectory(logDirectory);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var stdoutPath = Path.Combine(logDirectory, $"desktop-backend-{stamp}.stdout.log");
        var stderrPath = Path.Combine(logDirectory, $"desktop-backend-{stamp}.stderr.log");

        process.OutputDataReceived += (_, args) => AppendBackendLogLine(stdoutPath, args.Data);
        process.ErrorDataReceived += (_, args) => AppendBackendLogLine(stderrPath, args.Data);
        process.Exited += async (_, _) =>
        {
            AppendBackendLogLine(stderrPath, $"[host] backend exited with code {TryGetProcessExitCode(process)?.ToString() ?? "unknown"}");
            if (_isClosing || !_backendReady || !_ownsBackendProcess || !ReferenceEquals(process, _backendProcess))
            {
                return;
            }

            await RecoverBackendAsync("后端进程异常退出");
        };
    }

    private void AttachFinancialWorkerProcessDiagnostics(Process process, string dataRoot)
    {
        var logDirectory = _logDirectory ?? Path.Combine(dataRoot, "logs");
        Directory.CreateDirectory(logDirectory);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var stdoutPath = Path.Combine(logDirectory, $"desktop-financial-worker-{stamp}.stdout.log");
        var stderrPath = Path.Combine(logDirectory, $"desktop-financial-worker-{stamp}.stderr.log");

        process.OutputDataReceived += (_, args) => AppendBackendLogLine(stdoutPath, args.Data);
        process.ErrorDataReceived += (_, args) => AppendBackendLogLine(stderrPath, args.Data);
        process.Exited += (_, _) =>
        {
            AppendBackendLogLine(stderrPath, $"[host] financial worker exited with code {TryGetProcessExitCode(process)?.ToString() ?? "unknown"}");
        };
    }

    private void AppendBackendLogLine(string logPath, string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {content}{Environment.NewLine}";
        lock (_backendLogSync)
        {
            try
            {
                File.AppendAllText(logPath, line);
            }
            catch (IOException)
            {
                // Disk full or path unavailable – silently skip rather than crash the host
            }
        }
    }

    private static string ResolveLogDirectory(string defaultDataRoot)
    {
        var defaultLogDir = Path.Combine(defaultDataRoot, "logs");
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("logDirectory", out var prop) &&
                    prop.ValueKind == JsonValueKind.String)
                {
                    var configured = prop.GetString();
                    if (!string.IsNullOrWhiteSpace(configured))
                    {
                        Directory.CreateDirectory(configured);
                        return configured;
                    }
                }
            }
        }
        catch
        {
            // Fall back to default if settings file is corrupt
        }
        return defaultLogDir;
    }

    private static void MigrateLogsIfNeeded(string oldLogDir, string newLogDir)
    {
        if (string.Equals(Path.GetFullPath(oldLogDir), Path.GetFullPath(newLogDir), StringComparison.OrdinalIgnoreCase))
            return;
        if (!Directory.Exists(oldLogDir))
            return;

        try
        {
            Directory.CreateDirectory(newLogDir);
            foreach (var file in Directory.GetFiles(oldLogDir, "*.log"))
            {
                var dest = Path.Combine(newLogDir, Path.GetFileName(file));
                try { File.Move(file, dest, overwrite: true); } catch { /* skip locked files */ }
            }
            foreach (var file in Directory.GetFiles(oldLogDir, "*.txt"))
            {
                var dest = Path.Combine(newLogDir, Path.GetFileName(file));
                try { File.Move(file, dest, overwrite: true); } catch { }
            }
        }
        catch { /* best effort */ }
    }

    private void StartBackendHealthMonitoring()
    {
        if (_isClosing || !_ownsBackendProcess)
        {
            return;
        }

        _backendConsecutiveHealthFailures = 0;
        _backendHealthTimer.Stop();
        _backendHealthTimer.Start();
    }

    private void ReloadFrontend()
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(ReloadFrontend);
            return;
        }

        if (_webView.CoreWebView2 is not null)
        {
            _webView.CoreWebView2.Reload();
            return;
        }

        if (!string.IsNullOrWhiteSpace(_backendBaseUrl))
        {
            _webView.Source = new Uri(_backendBaseUrl);
        }
    }

    private void StopOwnedBackendProcess()
    {
        var process = _backendProcess;
        _backendProcess = null;
        _backendReady = false;
        _ownsBackendProcess = false;

        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
        }
        catch
        {
        }
        finally
        {
            process.Dispose();
        }

        // 兆底：按进程名查杀可能残留的 FinancialWorker
        try
        {
            foreach (var p in Process.GetProcessesByName("SimplerJiangAiAgent.FinancialWorker"))
            {
                try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
                p.Dispose();
            }
        }
        catch { }
    }

    private void StopOwnedFinancialWorkerProcess()
    {
        var process = _financialWorkerProcess;
        _financialWorkerProcess = null;

        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
        }
        catch
        {
        }
        finally
        {
            process.Dispose();
        }
    }

    private static int? TryGetProcessExitCode(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<bool> IsHealthyAsync(string baseUrl, string healthPath = "/api/health")
    {
        try
        {
            using var response = await HealthClient.GetAsync($"{baseUrl}{healthPath}");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string> GetStartupUrlAsync(string baseUrl)
    {
        var onboardingStatus = await GetOnboardingStatusAsync(baseUrl);
        if (onboardingStatus?.RequiresOnboarding != true)
        {
            return baseUrl;
        }

        var tabKey = string.IsNullOrWhiteSpace(onboardingStatus.RecommendedTabKey)
            ? "admin-llm"
            : onboardingStatus.RecommendedTabKey;

        return $"{baseUrl}/?tab={Uri.EscapeDataString(tabKey)}&onboarding=1";
    }

    private static async Task<LlmOnboardingStatusResponse?> GetOnboardingStatusAsync(string baseUrl)
    {
        try
        {
            using var response = await HealthClient.GetAsync($"{baseUrl}/api/llm/onboarding-status");
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            return await JsonSerializer.DeserializeAsync<LlmOnboardingStatusResponse>(stream, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        if (Debugger.IsAttached)
        {
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            var release = await AppUpdateService.GetAvailableReleaseAsync();
            if (release is null || IsDisposed)
            {
                return;
            }

            await PromptForUpdateAsync(release);
        }
        catch (Exception ex)
        {
            AppendDebug($"更新检查失败: {ex.Message}");
        }
    }

    private Task PromptForUpdateAsync(AppReleaseInfo release)
    {
        if (InvokeRequired)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            BeginInvoke(async () =>
            {
                try
                {
                    await PromptForUpdateAsync(release);
                    completion.SetResult();
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            });
            return completion.Task;
        }

        var message = $"发现新版本 v{release.VersionLabel}，当前版本为 v{AppUpdateService.CurrentVersionLabel}。\n\n{TrimReleaseNotes(release.ReleaseNotes)}\n\n是否立即从 GitHub 下载并更新？";
        var result = MessageBox.Show(this, message, "SimplerJiang AI Agent 更新", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (result != DialogResult.Yes)
        {
            return Task.CompletedTask;
        }

        return DownloadAndInstallUpdateAsync(release);
    }

    private async Task DownloadAndInstallUpdateAsync(AppReleaseInfo release)
    {
        UseWaitCursor = true;

        try
        {
            var installerPath = await AppUpdateService.DownloadInstallerAsync(release);
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS",
                UseShellExecute = true
            });

            if (process is null)
            {
                throw new InvalidOperationException("更新安装器启动失败。请手工前往 GitHub Release 页面下载安装。");
            }

            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"自动更新失败：{ex.Message}\n\n你也可以手工前往 {AppUpdateService.RepositoryUrl}/releases/latest 下载最新版本。",
                "SimplerJiang AI Agent 更新",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private static string TrimReleaseNotes(string? releaseNotes)
    {
        if (string.IsNullOrWhiteSpace(releaseNotes))
        {
            return "GitHub Release 已发布新版本。";
        }

        var lines = releaseNotes
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(8)
            .ToArray();

        if (lines.Length == 0)
        {
            return "GitHub Release 已发布新版本。";
        }

        var summary = string.Join(Environment.NewLine, lines);
        return summary.Length <= 500 ? summary : summary[..500] + "...";
    }

#if DEBUG
    private void ToggleDebugPanel()
    {
        _debugVisible = !_debugVisible;
        _debugTextBox.Visible = _debugVisible;
    }

    private void AppendDebug(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => AppendDebug(message));
            return;
        }

        _debugTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }
#else
    private void AppendDebug(string message)
    {
    }
#endif

    private sealed record BackendLaunchCommand(string FileName, string Arguments, string WorkingDirectory);

    private sealed record LlmOnboardingStatusResponse(
        bool HasAnyApiKey,
        bool RequiresOnboarding,
        string ActiveProviderKey,
        string RecommendedTabKey);
}
