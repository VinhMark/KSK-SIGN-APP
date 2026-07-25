using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;
using WpfMessageBox = System.Windows.MessageBox;

namespace KSKSigningManager;

public partial class MainWindow : Window
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "KSKSigningManager";

    private readonly string _serverDir;
    private readonly string _settingsPath;
    private readonly string _managerSettingsPath;
    private readonly string _logsDir;

    private Process? _serverProcess;
    private Forms.NotifyIcon? _trayIcon;
    private bool _allowRealClose;
    private bool _loaded;
    private ManagerSettings _managerSettings = new();

    public MainWindow()
    {
        InitializeComponent();

        _serverDir = FindServerDirectory();
        _settingsPath = Path.Combine(_serverDir, "appsettings.json");
        _managerSettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KSKSigningManager", "manager-settings.json");
        _logsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KSKSigningManager", "Logs");

        Directory.CreateDirectory(Path.GetDirectoryName(_managerSettingsPath)!);
        Directory.CreateDirectory(_logsDir);

        InitializeTrayIcon();
        RefreshCertificates();
        LoadServerSettings();
        LoadManagerSettings();
        UpdateAddress();
        SetServerState(ServerUiState.Stopped, "Đã dừng");
        WriteLog("INFO", "Ứng dụng quản lý Signing Server đã khởi động.");
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;

        if (_managerSettings.StartMinimized)
            HideToTray(showNotification: false);

        if (_managerSettings.AutoStartServer)
            await StartServerAsync(showSuccessDialog: false);
    }

    private void InitializeTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Mở Signing Server", null, (_, _) => Dispatcher.Invoke(ShowFromTray));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Khởi động server", null, async (_, _) => await Dispatcher.InvokeAsync(() => StartServerAsync(false)));
        menu.Items.Add("Dừng server", null, (_, _) => Dispatcher.Invoke(StopServer));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Thoát hoàn toàn", null, (_, _) => Dispatcher.Invoke(ExitApplication));

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "KSK Signing Server",
            Visible = true,
            ContextMenuStrip = menu
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowRealClose) return;
        e.Cancel = true;
        HideToTray(showNotification: true);
    }

    private void HideToTray(bool showNotification)
    {
        Hide();
        ShowInTaskbar = false;

        if (showNotification && _trayIcon is not null)
        {
            _trayIcon.BalloonTipTitle = "KSK Signing Server";
            _trayIcon.BalloonTipText = IsServerRunning() ? "Server vẫn đang chạy nền." : "Ứng dụng đã thu nhỏ xuống khay hệ thống.";
            _trayIcon.ShowBalloonTip(2500);
        }
    }

    private void ShowFromTray()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void ExitApplication()
    {
        try
        {
            StopServer();
            _trayIcon?.Dispose();
            _trayIcon = null;
        }
        finally
        {
            _allowRealClose = true;
            WpfApplication.Current.Shutdown();
        }
    }

    private static string FindServerDirectory()
    {
        var searched = new List<string>();

        static IEnumerable<string> WalkParents(string startPath)
        {
            var current = new DirectoryInfo(Path.GetFullPath(startPath));
            while (current is not null)
            {
                yield return current.FullName;
                current = current.Parent;
            }
        }

        var roots = WalkParents(AppContext.BaseDirectory)
            .Concat(WalkParents(Directory.GetCurrentDirectory()))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            var candidate = Path.Combine(root, "KSKSigningServer");
            searched.Add(candidate);

            if (File.Exists(Path.Combine(candidate, "KSKSigningServer.exe")) ||
                File.Exists(Path.Combine(candidate, "KSKSigningServer.dll")) ||
                File.Exists(Path.Combine(candidate, "KSKSigningServer.csproj")))
            {
                return candidate;
            }
        }

        var bundledCandidate = Path.Combine(AppContext.BaseDirectory, "KSKSigningServer");
        searched.Add(bundledCandidate);

        throw new DirectoryNotFoundException(
            "Không tìm thấy thư mục KSKSigningServer.`n`nĐã kiểm tra:`n- " +
            string.Join("`n- ", searched.Distinct(StringComparer.OrdinalIgnoreCase)));
    }

    private void RefreshCertificates_Click(object sender, RoutedEventArgs e) => RefreshCertificates();

    private void RefreshCertificates()
    {
        try
        {
            var selectedThumbprint = ThumbprintText.Text?.Trim() ?? "";
            var items = new List<CertificateItem>();

            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);

            foreach (var cert in store.Certificates.Cast<X509Certificate2>()
                         .Where(c => c.HasPrivateKey &&
                             (c.Issuer.Contains("MISA-CA", StringComparison.OrdinalIgnoreCase) ||
                              c.Subject.Contains("MISA-CA", StringComparison.OrdinalIgnoreCase))))
            {
                items.Add(new CertificateItem(cert));
            }

            CertificateCombo.ItemsSource = items;
            var match = items.FirstOrDefault(x => x.Thumbprint.Equals(selectedThumbprint, StringComparison.OrdinalIgnoreCase));
            if (match is not null) CertificateCombo.SelectedItem = match;
            else if (items.Count > 0) CertificateCombo.SelectedIndex = 0;

            if (items.Count == 0)
            {
                CertificateStatusText.Text = "Không tìm thấy chứng thư MISA-CA có private key.";
                WriteLog("WARNING", "Không tìm thấy chứng thư MISA-CA có private key.");
            }
            else WriteLog("INFO", $"Đã quét thấy {items.Count} chứng thư MISA-CA.");
        }
        catch (Exception ex)
        {
            CertificateStatusText.Text = "Lỗi quét chứng thư: " + ex.Message;
            WriteLog("ERROR", "Lỗi quét chứng thư: " + ex.Message);
        }
    }

    private void CertificateCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CertificateCombo.SelectedItem is not CertificateItem item) return;
        ThumbprintText.Text = item.Thumbprint;
        CertificateStatusText.Text = item.IsExpired
            ? $"{item.Subject} — ĐÃ HẾT HẠN {item.NotAfter:dd/MM/yyyy}"
            : $"{item.Subject} — còn hạn đến {item.NotAfter:dd/MM/yyyy}";
    }

    private void GenerateApiKey_Click(object sender, RoutedEventArgs e)
    {
        ApiKeyText.Text = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        UpdateAddress();
    }
    private void CopyApiKey_Click(object sender, RoutedEventArgs e)
    {
        var apiKey = ApiKeyText.Text?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            WpfMessageBox.Show(
                "API Key đang trống.",
                "Không thể sao chép",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        System.Windows.Clipboard.SetText(apiKey);
        WriteLog("INFO", "Đã sao chép API Key vào Clipboard.");
        WpfMessageBox.Show(
            "Đã sao chép API Key.",
            "Thành công",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void LoadServerSettings()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                GenerateApiKey_Click(this, new RoutedEventArgs());
                return;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(_settingsPath));
            var s = doc.RootElement.GetProperty("SigningServer");
            var urls = s.TryGetProperty("Urls", out var u) ? u.GetString() ?? "" : "";
            if (Uri.TryCreate(urls.Replace("0.0.0.0", "127.0.0.1"), UriKind.Absolute, out var uri)) PortText.Text = uri.Port.ToString();

            ApiKeyText.Text = s.TryGetProperty("ApiKey", out var a) ? a.GetString() ?? "" : "";
            var thumb = s.TryGetProperty("CertificateThumbprint", out var t) ? t.GetString() ?? "" : "";
            ThumbprintText.Text = thumb;
            var match = CertificateCombo.Items.Cast<CertificateItem>().FirstOrDefault(x => x.Thumbprint.Equals(thumb, StringComparison.OrdinalIgnoreCase));
            if (match is not null) CertificateCombo.SelectedItem = match;

            if (string.IsNullOrWhiteSpace(ApiKeyText.Text) || ApiKeyText.Text.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
                GenerateApiKey_Click(this, new RoutedEventArgs());

            WriteLog("INFO", "Đã tải cấu hình Signing Server.");
        }
        catch (Exception ex)
        {
            WriteLog("WARNING", "Không đọc được cấu hình server: " + ex.Message);
        }
    }

    private void LoadManagerSettings()
    {
        try
        {
            if (File.Exists(_managerSettingsPath))
                _managerSettings = JsonSerializer.Deserialize<ManagerSettings>(File.ReadAllText(_managerSettingsPath)) ?? new ManagerSettings();
        }
        catch (Exception ex)
        {
            _managerSettings = new ManagerSettings();
            WriteLog("WARNING", "Cấu hình giao diện bị lỗi, đã dùng mặc định: " + ex.Message);
        }

        AutoStartWindowsCheck.IsChecked = _managerSettings.AutoStartWindows;
        StartMinimizedCheck.IsChecked = _managerSettings.StartMinimized;
        AutoStartServerCheck.IsChecked = _managerSettings.AutoStartServer;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveConfiguration();
            WriteLog("SUCCESS", "Đã lưu cấu hình thành công.");
            WpfMessageBox.Show("Đã lưu cấu hình Signing Server.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            WriteLog("ERROR", "Không thể lưu cấu hình: " + ex.Message);
            WpfMessageBox.Show(ex.Message, "Không thể lưu", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveConfiguration()
    {
        if (!int.TryParse(PortText.Text, out var port) || port is < 1024 or > 65535)
            throw new InvalidOperationException("Cổng Server phải từ 1024 đến 65535.");
        if (string.IsNullOrWhiteSpace(ApiKeyText.Text) || ApiKeyText.Text.Length < 24)
            throw new InvalidOperationException("API Key phải có ít nhất 24 ký tự.");
        if (string.IsNullOrWhiteSpace(ThumbprintText.Text))
            throw new InvalidOperationException("Chưa chọn chứng thư MISA-CA.");

        var encryptedPin = "";
        if (RememberPinCheck.IsChecked == true && !string.IsNullOrEmpty(PinBox.Password))
        {
            var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(PinBox.Password), null, DataProtectionScope.CurrentUser);
            encryptedPin = Convert.ToBase64String(protectedBytes);
        }
        else if (File.Exists(_settingsPath))
        {
            try
            {
                using var old = JsonDocument.Parse(File.ReadAllText(_settingsPath));
                encryptedPin = old.RootElement.GetProperty("SigningServer").GetProperty("EncryptedPin").GetString() ?? "";
            }
            catch { }
        }

        var serverConfig = new
        {
            SigningServer = new
            {
                Urls = $"http://0.0.0.0:{port}",
                ApiKey = ApiKeyText.Text.Trim(),
                AllowedIps = ResolveAllowedIps(),
                CertificateThumbprint = ThumbprintText.Text.Trim(),
                CertificateSubjectContains = "MISA-CA",
                StoreLocation = "CurrentUser",
                RequireKskRoot = true,
                FacilityCode = "",
                MaxRequestBytes = 2097152,
                EncryptedPin = encryptedPin
            }
        };

        Directory.CreateDirectory(_serverDir);
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(serverConfig, new JsonSerializerOptions { WriteIndented = true }));

        _managerSettings = new ManagerSettings
        {
            AutoStartWindows = AutoStartWindowsCheck.IsChecked == true,
            StartMinimized = StartMinimizedCheck.IsChecked == true,
            AutoStartServer = AutoStartServerCheck.IsChecked == true
        };
        File.WriteAllText(_managerSettingsPath, JsonSerializer.Serialize(_managerSettings, new JsonSerializerOptions { WriteIndented = true }));
        ConfigureWindowsStartup(_managerSettings.AutoStartWindows);
        UpdateAddress();
    }

    private void ConfigureWindowsStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
        {
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Không xác định được đường dẫn ứng dụng.");
            key.SetValue(RunValueName, $"\"{executable}\"");
        }
        else key.DeleteValue(RunValueName, throwOnMissingValue: false);
    }

    private string[] ResolveAllowedIps()
    {
        var tag = (AccessModeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        if (tag == "LOCAL") return ["127.0.0.1", "::1"];
        if (tag == "CUSTOM") return AllowedIpsText.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var ip = GetLanIp();
        if (ip is null) return ["127.0.0.1", "::1"];
        var parts = ip.Split('.');
        return ["127.0.0.1", "::1", $"{parts[0]}.{parts[1]}.{parts[2]}.0/24"];
    }

    private async void StartServer_Click(object sender, RoutedEventArgs e) => await StartServerAsync(true);

    private async Task StartServerAsync(bool showSuccessDialog)
    {
        if (IsServerRunning())
        {
            WriteLog("INFO", "Yêu cầu Start bị bỏ qua vì server đang chạy.");
            return;
        }

        try
        {
            SaveConfiguration();
            if (!int.TryParse(PortText.Text, out var port)) throw new InvalidOperationException("Cổng Server không hợp lệ.");
            if (IsPortInUse(port)) throw new InvalidOperationException($"Cổng {port} đang được một tiến trình khác sử dụng.");

            SetServerState(ServerUiState.Starting, "Đang khởi động");
            WriteLog("INFO", $"Đang khởi động Signing Server tại cổng {port}.");

            var exe = Path.Combine(_serverDir, "KSKSigningServer.exe");
            var dll = Path.Combine(_serverDir, "KSKSigningServer.dll");
            var project = Path.Combine(_serverDir, "KSKSigningServer.csproj");

            ProcessStartInfo psi;
            if (File.Exists(exe))
            {
                psi = new ProcessStartInfo(exe)
                {
                    WorkingDirectory = _serverDir,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
            }
            else if (File.Exists(dll))
            {
                psi = new ProcessStartInfo("dotnet", $"\"{dll}\"")
                {
                    WorkingDirectory = _serverDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
            }
            else if (File.Exists(project))
            {
                psi = new ProcessStartInfo("dotnet", $"run --project \"{project}\"")
                {
                    WorkingDirectory = _serverDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
            }
            else
            {
                throw new FileNotFoundException(
                    "Đã tìm thấy thư mục server nhưng không có KSKSigningServer.exe, KSKSigningServer.dll hoặc KSKSigningServer.csproj.",
                    _serverDir);
            }

            _serverProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _serverProcess.Exited += ServerProcess_Exited;
            if (!_serverProcess.Start()) throw new InvalidOperationException("Không thể khởi động tiến trình Signing Server.");

            if (psi.RedirectStandardOutput)
            {
                _serverProcess.OutputDataReceived += (_, args) => { if (!string.IsNullOrWhiteSpace(args.Data)) Dispatcher.Invoke(() => WriteLog("INFO", "[SERVER] " + args.Data)); };
                _serverProcess.ErrorDataReceived += (_, args) => { if (!string.IsNullOrWhiteSpace(args.Data)) Dispatcher.Invoke(() => WriteLog("ERROR", "[SERVER] " + args.Data)); };
                _serverProcess.BeginOutputReadLine();
                _serverProcess.BeginErrorReadLine();
            }

            await WaitForServerReadyAsync();
            SetServerState(ServerUiState.Running, "Đang hoạt động");
            WriteLog("SUCCESS", "Signing Server đã hoạt động. Token chưa được kích hoạt; Client sẽ nhập PIN khi cần.");
            if (showSuccessDialog)
                WpfMessageBox.Show("Signing Server đã khởi động. Token sẽ chỉ được kích hoạt khi Client gửi PIN hợp lệ.", "Server sẵn sàng", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StopServerProcessOnly();
            SetServerState(ServerUiState.Error, "Lỗi");
            WriteLog("ERROR", "Khởi động server thất bại: " + ex.Message);
            if (showSuccessDialog)
                WpfMessageBox.Show(ex.Message, "Không thể khởi động Signing Server", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ServerProcess_Exited(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (_allowRealClose) return;
            var exitCode = _serverProcess?.ExitCode;
            SetServerState(ServerUiState.Error, "Tiến trình đã dừng");
            WriteLog("ERROR", $"Signing Server đã thoát ngoài dự kiến. ExitCode={exitCode?.ToString() ?? "N/A"}");
        });
    }

    private async Task WaitForServerReadyAsync()
    {
        if (!int.TryParse(PortText.Text, out var port))
            throw new InvalidOperationException("Cổng Server không hợp lệ.");

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-API-Key", ApiKeyText.Text.Trim());
        var statusUrl = $"http://127.0.0.1:{port}/api/status";
        Exception? lastError = null;

        for (var attempt = 1; attempt <= 30; attempt++)
        {
            try
            {
                using var statusResponse = await client.GetAsync(statusUrl);
                if (statusResponse.IsSuccessStatusCode)
                    return;

                lastError = new InvalidOperationException($"Server trả HTTP {(int)statusResponse.StatusCode}.");
            }
            catch (HttpRequestException ex) { lastError = ex; }
            catch (TaskCanceledException ex) { lastError = ex; }

            await Task.Delay(500);
        }

        throw new InvalidOperationException(
            "Không kết nối được Signing Server sau 15 giây. " +
            (lastError?.Message ?? "Không rõ nguyên nhân."));
    }

    private static string ReadServerMessage(string json, string? fallback)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("message", out var message)) return message.GetString() ?? fallback ?? "Kiểm tra Token thất bại.";
        }
        catch { }
        return string.IsNullOrWhiteSpace(json) ? fallback ?? "Kiểm tra Token thất bại." : json;
    }

    private void StopServer_Click(object sender, RoutedEventArgs e) => StopServer();

    private void StopServer()
    {
        if (!IsServerRunning())
        {
            SetServerState(ServerUiState.Stopped, "Đã dừng");
            return;
        }

        try
        {
            WriteLog("INFO", "Đang dừng Signing Server.");
            StopServerProcessOnly();
            SetServerState(ServerUiState.Stopped, "Đã dừng");
            WriteLog("SUCCESS", "Signing Server đã dừng.");
        }
        catch (Exception ex)
        {
            SetServerState(ServerUiState.Error, "Lỗi khi dừng");
            WriteLog("ERROR", "Không thể dừng server: " + ex.Message);
            WpfMessageBox.Show(ex.Message, "Không thể dừng server", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void StopServerProcessOnly()
    {
        if (_serverProcess is not { HasExited: false })
        {
            _serverProcess?.Dispose();
            _serverProcess = null;
            return;
        }

        _serverProcess.Exited -= ServerProcess_Exited;
        try
        {
            _serverProcess.CloseMainWindow();
            if (!_serverProcess.WaitForExit(3000))
            {
                _serverProcess.Kill(entireProcessTree: true);
                _serverProcess.WaitForExit(5000);
            }
        }
        finally
        {
            _serverProcess.Dispose();
            _serverProcess = null;
        }
    }

    private bool IsServerRunning() => _serverProcess is { HasExited: false };
    private static bool IsPortInUse(int port) => IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(endpoint => endpoint.Port == port);

    private void SetServerState(ServerUiState state, string text)
    {
        var (background, dot, foreground) = state switch
        {
            ServerUiState.Running => ("#DCFCE7", "#16A34A", "#166534"),
            ServerUiState.Starting => ("#DBEAFE", "#2563EB", "#1D4ED8"),
            ServerUiState.Error => ("#FEE2E2", "#DC2626", "#991B1B"),
            _ => ("#E5E7EB", "#6B7280", "#374151")
        };

        ServerStateBadge.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(background));
        StateDot.Fill = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(dot));
        ServerStateText.Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(foreground));
        ServerStateText.Text = text.ToUpperInvariant();
        StartButton.IsEnabled = state is ServerUiState.Stopped or ServerUiState.Error;
        StopButton.IsEnabled = state is ServerUiState.Running or ServerUiState.Starting;
        ProcessStatusText.Text = state switch
        {
            ServerUiState.Running => $"PID {_serverProcess?.Id}",
            ServerUiState.Starting => "Đang khởi tạo...",
            ServerUiState.Error => "Có lỗi",
            _ => "Chưa chạy"
        };

        if (_trayIcon is not null)
            _trayIcon.Text = state == ServerUiState.Running ? "KSK Signing Server — Đang chạy" : "KSK Signing Server — Đã dừng";
    }

    private void PortText_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsInitialized) UpdateAddress();
    }

    private void UpdateAddress()
    {
        var ip = GetLanIp() ?? "127.0.0.1";
        var port = string.IsNullOrWhiteSpace(PortText.Text) ? "7443" : PortText.Text.Trim();
        AddressText.Text = $"http://{ip}:{port}";
    }

    private static string? GetLanIp()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.ToString().StartsWith("169.254", StringComparison.Ordinal))?.ToString();
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_logsDir);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_logsDir}\"") { UseShellExecute = true });
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e) => LogList.Items.Clear();

    private void WriteLog(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {level,-7} | {message}";
        LogList.Items.Add(line);
        while (LogList.Items.Count > 1000) LogList.Items.RemoveAt(0);
        if (LogList.Items.Count > 0) LogList.ScrollIntoView(LogList.Items[^1]);

        try
        {
            Directory.CreateDirectory(_logsDir);
            var file = Path.Combine(_logsDir, $"manager-{DateTime.Now:yyyy-MM-dd}.log");
            File.AppendAllText(file, line + Environment.NewLine, Encoding.UTF8);
        }
        catch { }
    }

    private sealed class CertificateItem
    {
        public CertificateItem(X509Certificate2 cert)
        {
            Thumbprint = cert.Thumbprint ?? "";
            Subject = cert.GetNameInfo(X509NameType.SimpleName, false);
            NotAfter = cert.NotAfter;
            IsExpired = cert.NotAfter <= DateTime.Now;
            DisplayName = $"{Subject} | Hết hạn {NotAfter:dd/MM/yyyy}" + (IsExpired ? " | ĐÃ HẾT HẠN" : "");
        }

        public string Thumbprint { get; }
        public string Subject { get; }
        public DateTime NotAfter { get; }
        public bool IsExpired { get; }
        public string DisplayName { get; }
    }

    private sealed class ManagerSettings
    {
        public bool AutoStartWindows { get; set; }
        public bool StartMinimized { get; set; }
        public bool AutoStartServer { get; set; }
    }

    private enum ServerUiState
    {
        Stopped,
        Starting,
        Running,
        Error
    }
}


