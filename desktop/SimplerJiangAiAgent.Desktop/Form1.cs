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
    private const int BackendHealthFailureThreshold = 3;
    private static readonly TimeSpan BackendStartupTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan BackendHealthProbeInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan BackendHealthProbeTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan BackendHealthRecoveryGracePeriod = TimeSpan.FromSeconds(20);
    private static readonly HttpClient HealthClient = new() { Timeout = BackendHealthProbeTimeout };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly WebView2 _webView;
    private readonly System.Windows.Forms.Timer _backendHealthTimer;
    private readonly object _backendLogSync = new();
    private Process? _backendProcess;
    private BackendLaunchCommand? _backendLaunchCommand;
    private string? _backendBaseUrl;
    private string? _backendDataRoot;
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

        if (await IsHealthyAsync(existingUrl))
        {
            _ownsBackendProcess = false;
            _backendReady = true;
            _lastBackendHealthyAtUtc = DateTime.UtcNow;
            AppendDebug($"复用已运行后端: {existingUrl}");
            return existingUrl;
        }

        _backendLaunchCommand = FindPackagedBackendLaunchCommand();
        if (_backendLaunchCommand is null)
        {
            throw new InvalidOperationException("未找到可启动的后端程序。请使用打包后的发布目录运行桌面程序，或先手工启动本地后端。");
        }

        _backendDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SimplerJiangAiAgent");
        Directory.CreateDirectory(_backendDataRoot);

        await StartManagedBackendAsync(existingUrl, _backendDataRoot);
        StartBackendHealthMonitoring();
        return existingUrl;
    }

    private void OnFormClosed(object? sender, FormClosedEventArgs e)
    {
        _isClosing = true;
        _backendHealthTimer.Stop();
        StopOwnedBackendProcess();
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

    private static async Task<bool> WaitForHealthyAsync(string baseUrl, Process process, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                return false;
            }

            if (await IsHealthyAsync(baseUrl))
            {
                return true;
            }

            await Task.Delay(500);
        }

        return false;
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

        var process = CreateBackendProcess(_backendLaunchCommand, baseUrl, dataRoot);
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

    private static Process CreateBackendProcess(BackendLaunchCommand launchCommand, string baseUrl, string dataRoot)
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

        return new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
    }

    private void AttachBackendProcessDiagnostics(Process process, string dataRoot)
    {
        var logDirectory = Path.Combine(dataRoot, "logs");
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

    private void AppendBackendLogLine(string logPath, string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {content}{Environment.NewLine}";
        lock (_backendLogSync)
        {
            File.AppendAllText(logPath, line);
        }
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

    private static async Task<bool> IsHealthyAsync(string baseUrl)
    {
        try
        {
            using var response = await HealthClient.GetAsync($"{baseUrl}/api/health");
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
