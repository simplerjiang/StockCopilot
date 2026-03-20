using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Web.WebView2.WinForms;

namespace SimplerJiangAiAgent.Desktop;

public partial class Form1 : Form
{
    private const int PreferredBackendPort = 5119;
    private static readonly HttpClient HealthClient = new() { Timeout = TimeSpan.FromSeconds(2) };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly WebView2 _webView;
    private Process? _backendProcess;
#if DEBUG
    private readonly TextBox _debugTextBox;
    private readonly Button _debugButton;
    private bool _debugVisible;
#endif

    public Form1()
    {
        InitializeComponent();

        Text = "SimplerJiang AI Agent";
        WindowState = FormWindowState.Maximized;

        _webView = new WebView2
        {
            Dock = DockStyle.Fill
        };

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
#if DEBUG
            _webView.CoreWebView2.ProcessFailed += (_, args) => AppendDebug($"WebView2 进程异常: {args.Reason}");
            _webView.CoreWebView2.NavigationCompleted += (_, args) =>
            {
                if (!args.IsSuccess)
                {
                    AppendDebug($"页面加载失败: {args.WebErrorStatus}");
                }
            };

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
        if (await IsHealthyAsync(existingUrl))
        {
            AppendDebug($"复用已运行后端: {existingUrl}");
            return existingUrl;
        }

        var launchCommand = FindPackagedBackendLaunchCommand();
        if (launchCommand is null)
        {
            throw new InvalidOperationException("未找到可启动的后端程序。请使用打包后的发布目录运行桌面程序，或先手工启动本地后端。");
        }

        var port = FindAvailablePort(PreferredBackendPort, PreferredBackendPort + 20);
        var baseUrl = $"http://localhost:{port}";
        var dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SimplerJiangAiAgent");
        Directory.CreateDirectory(dataRoot);

        var startInfo = new ProcessStartInfo
        {
            FileName = launchCommand.FileName,
            Arguments = launchCommand.Arguments,
            WorkingDirectory = launchCommand.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        startInfo.Environment["DOTNET_ENVIRONMENT"] = "Production";
        startInfo.Environment["ASPNETCORE_URLS"] = baseUrl;
        startInfo.Environment["SJAI_DATA_ROOT"] = dataRoot;

        _backendProcess = Process.Start(startInfo);
        if (_backendProcess is null)
        {
            throw new InvalidOperationException("后端启动失败，未能创建后端进程。");
        }

        AppendDebug($"已启动本地后端进程: {_backendProcess.Id}, 地址: {baseUrl}");
        if (!await WaitForHealthyAsync(baseUrl, _backendProcess, TimeSpan.FromSeconds(20)))
        {
            throw new InvalidOperationException("后端启动超时，请检查发布目录或日志。");
        }

        return baseUrl;
    }

    private void OnFormClosed(object? sender, FormClosedEventArgs e)
    {
        if (_backendProcess is null)
        {
            return;
        }

        try
        {
            if (!_backendProcess.HasExited)
            {
                _backendProcess.Kill(entireProcessTree: true);
                _backendProcess.WaitForExit(3000);
            }
        }
        catch
        {
        }
        finally
        {
            _backendProcess.Dispose();
            _backendProcess = null;
        }
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

    private static int FindAvailablePort(int startInclusive, int endExclusive)
    {
        for (var port = startInclusive; port < endExclusive; port++)
        {
            if (IsPortAvailable(port))
            {
                return port;
            }
        }

        throw new InvalidOperationException($"未找到可用本地端口，尝试范围 {startInclusive}-{endExclusive - 1}。");
    }

    private static bool IsPortAvailable(int port)
    {
        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(System.Net.IPAddress.Loopback, port);
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        finally
        {
            listener?.Stop();
        }
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
