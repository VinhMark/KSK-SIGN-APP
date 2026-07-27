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
    private readonly Pkcs11DiscoveryService _pkcs11Discovery = new();
    private string _savedTokenSerial = "";
    private string _savedCertificateThumbprint = "";

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
        LoadServerSettings();
        RefreshTokens(showDialog: false);
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
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "KSKSigningServer"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "KSKSigningServer")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "KSKSigningServer")),
            Path.Combine(Directory.GetCurrentDirectory(), "KSKSigningServer")
        };

        return candidates.FirstOrDefault(Directory.Exists) ?? Path.Combine(AppContext.BaseDirectory, "KSKSigningServer");
    }

    private void BrowseLibrary_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Chọn thư viện PKCS#11",
            Filter = "PKCS#11 library (*.dll)|*.dll|Tất cả tệp (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            LibraryPathText.Text = dialog.FileName;
            RefreshTokens(showDialog: true);
        }
    }

    private void TestLibrary_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var tokens = _pkcs11Discovery.GetTokens(LibraryPathText.Text.Trim());
            WriteLog("SUCCESS", $"Đã nạp DLL PKCS#11. Tìm thấy {tokens.Count} Token.");
            WpfMessageBox.Show(
                $"Thư viện PKCS#11 hợp lệ.\nTìm thấy {tokens.Count} Token đang kết nối.",
                "Kiểm tra DLL thành công",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            WriteLog("ERROR", "Kiểm tra DLL thất bại: " + ex.Message);
            WpfMessageBox.Show(ex.Message, "DLL PKCS#11 không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RefreshTokens_Click(object sender, RoutedEventArgs e) => RefreshTokens(showDialog: true);

    private void RefreshTokens(bool showDialog)
    {
        try
        {
            var path = LibraryPathText.Text.Trim();
            var tokens = _pkcs11Discovery.GetTokens(path);
            TokenCombo.ItemsSource = tokens;
            CertificateCombo.ItemsSource = null;
            CertificateDetailsText.Clear();
            CertificateStatusText.Text = tokens.Count == 0
                ? "Không tìm thấy USB Token."
                : $"Đã tìm thấy {tokens.Count} USB Token.";

            var selected = tokens.FirstOrDefault(x =>
                string.Equals(x.Serial, _savedTokenSerial, StringComparison.OrdinalIgnoreCase));
            if (selected is not null) TokenCombo.SelectedItem = selected;
            else if (tokens.Count > 0) TokenCombo.SelectedIndex = 0;

            WriteLog(tokens.Count > 0 ? "SUCCESS" : "WARNING", $"Quét PKCS#11: tìm thấy {tokens.Count} Token.");
            if (showDialog && tokens.Count == 0)
                WpfMessageBox.Show("DLL đã nạp được nhưng không tìm thấy USB Token đang kết nối.", "Không tìm thấy Token", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            TokenCombo.ItemsSource = null;
            CertificateCombo.ItemsSource = null;
            CertificateDetailsText.Clear();
            CertificateStatusText.Text = "Lỗi PKCS#11: " + ex.Message;
            WriteLog("ERROR", "Quét Token thất bại: " + ex.Message);
            if (showDialog)
                WpfMessageBox.Show(ex.Message, "Không thể quét Token", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void TokenCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TokenCombo.SelectedItem is not Pkcs11TokenItem token)
        {
            CertificateCombo.ItemsSource = null;
            return;
        }

        try
        {
            var certificates = _pkcs11Discovery.GetCertificates(
                LibraryPathText.Text.Trim(), token.Serial, token.SlotId);
            CertificateCombo.ItemsSource = certificates;

            var selected = certificates.FirstOrDefault(x =>
                string.Equals(x.Thumbprint, _savedCertificateThumbprint, StringComparison.OrdinalIgnoreCase));
            if (selected is not null) CertificateCombo.SelectedItem = selected;
            else if (certificates.Count > 0) CertificateCombo.SelectedIndex = 0;

            CertificateStatusText.Text = certificates.Count == 0
                ? $"Token {token.Label} không có chứng thư X.509 đọc được."
                : $"{token.Label} — Serial {token.Serial}";
            WriteLog(certificates.Count > 0 ? "INFO" : "WARNING",
                $"Token {token.Label} ({token.Serial}): tìm thấy {certificates.Count} chứng thư.");
        }
        catch (Exception ex)
        {
            CertificateCombo.ItemsSource = null;
            CertificateDetailsText.Clear();
            CertificateStatusText.Text = "Không đọc được chứng thư: " + ex.Message;
            WriteLog("ERROR", "Không đọc được chứng thư Token: " + ex.Message);
        }
    }

    private void CertificateCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CertificateCombo.SelectedItem is not Pkcs11CertificateItem item)
        {
            CertificateDetailsText.Clear();
            return;
        }

        CertificateDetailsText.Text =
            $"Chủ thể: {item.FullSubject}\n" +
            $"Nhà cấp: {item.Issuer}\n" +
            $"Serial: {item.SerialNumber}\n" +
            $"Thumbprint: {item.Thumbprint}\n" +
            $"Hiệu lực: {item.NotBefore:dd/MM/yyyy} - {item.NotAfter:dd/MM/yyyy}";

        CertificateStatusText.Text = item.IsExpired
            ? $"{item.Subject} — ĐÃ HẾT HẠN {item.NotAfter:dd/MM/yyyy}"
            : item.IsNotYetValid
                ? $"{item.Subject} — CHƯA ĐẾN NGÀY HIỆU LỰC"
                : $"{item.Subject} — còn hạn đến {item.NotAfter:dd/MM/yyyy}";
    }

    private void LibraryPathText_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsInitialized) return;
        TokenCombo.ItemsSource = null;
        CertificateCombo.ItemsSource = null;
        CertificateDetailsText.Clear();
        CertificateStatusText.Text = "DLL đã thay đổi — bấm Quét lại.";
    }

    private void GenerateApiKey_Click(object sender, RoutedEventArgs e)
    {
        ApiKeyBox.Password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        UpdateAddress();
    }

    private void CopyApiKey_Click(object sender, RoutedEventArgs e)
    {
        var apiKey = ApiKeyBox.Password?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            System.Windows.MessageBox.Show(
                "Chưa có API Key để sao chép. Hãy bấm Tạo mới trước.",
                "API Key",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(apiKey);
            System.Windows.MessageBox.Show(
                "Đã sao chép API Key vào Clipboard.",
                "API Key",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Không thể sao chép API Key: {ex.Message}",
                "API Key",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void LoadServerSettings()
    {
        LibraryPathText.Text = @"C:\Windows\System32\misaca_csp11_v2.dll";

        try
        {
            if (!File.Exists(_settingsPath))
            {
                GenerateApiKey_Click(this, new RoutedEventArgs());
                return;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(_settingsPath));
            var config = doc.RootElement.GetProperty("SigningServer");
            var urls = config.TryGetProperty("Urls", out var urlsNode) ? urlsNode.GetString() ?? "" : "";
            if (Uri.TryCreate(urls.Replace("0.0.0.0", "127.0.0.1"), UriKind.Absolute, out var uri))
                PortText.Text = uri.Port.ToString();

            ApiKeyBox.Password = config.TryGetProperty("ApiKey", out var apiKeyNode)
                ? apiKeyNode.GetString() ?? ""
                : "";
            LibraryPathText.Text = config.TryGetProperty("Pkcs11LibraryPath", out var libraryNode)
                ? libraryNode.GetString() ?? LibraryPathText.Text
                : LibraryPathText.Text;
            _savedTokenSerial = config.TryGetProperty("TokenSerial", out var serialNode)
                ? serialNode.GetString() ?? ""
                : "";
            _savedCertificateThumbprint = config.TryGetProperty("CertificateThumbprint", out var thumbNode)
                ? thumbNode.GetString() ?? ""
                : "";
            var encryptedPin = config.TryGetProperty("EncryptedPin", out var pinNode)
                ? pinNode.GetString() ?? ""
                : "";
            TokenPinBox.Password = PinProtection.TryUnprotect(encryptedPin, out var pin)
                ? pin
                : "";
            if (!string.IsNullOrWhiteSpace(encryptedPin) && string.IsNullOrWhiteSpace(TokenPinBox.Password))
                WriteLog("WARNING", "Không giải mã được PIN đã lưu. Hãy nhập lại PIN và lưu cấu hình.");

            if (string.IsNullOrWhiteSpace(ApiKeyBox.Password) ||
                ApiKeyBox.Password.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
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
        if (string.IsNullOrWhiteSpace(ApiKeyBox.Password) || ApiKeyBox.Password.Length < 24)
            throw new InvalidOperationException("API Key phải có ít nhất 24 ký tự.");
        if (string.IsNullOrWhiteSpace(TokenPinBox.Password))
            throw new InvalidOperationException("Chưa nhập mã PIN USB Token trên Server.");
        if (string.IsNullOrWhiteSpace(LibraryPathText.Text) || !File.Exists(LibraryPathText.Text.Trim()))
            throw new InvalidOperationException("Chưa chọn DLL PKCS#11 hợp lệ.");
        if (TokenCombo.SelectedItem is not Pkcs11TokenItem selectedToken)
            throw new InvalidOperationException("Chưa chọn USB Token.");
        if (CertificateCombo.SelectedItem is not Pkcs11CertificateItem selectedCertificate)
            throw new InvalidOperationException("Chưa chọn chứng thư ký trong USB Token.");
        if (selectedCertificate.IsExpired)
            throw new InvalidOperationException($"Chứng thư đã hết hạn ngày {selectedCertificate.NotAfter:dd/MM/yyyy}.");
        if (selectedCertificate.IsNotYetValid)
            throw new InvalidOperationException($"Chứng thư chưa có hiệu lực. Ngày bắt đầu: {selectedCertificate.NotBefore:dd/MM/yyyy}.");

        var serverConfig = new
        {
            SigningServer = new
            {
                Urls = $"http://0.0.0.0:{port}",
                ApiKey = ApiKeyBox.Password.Trim(),
                AllowedIps = ResolveAllowedIps(),
                Pkcs11LibraryPath = LibraryPathText.Text.Trim(),
                TokenLabelContains = selectedToken.Label,
                TokenSerial = selectedToken.Serial,
                CertificateThumbprint = selectedCertificate.Thumbprint,
                CertificateSubjectContains = "",
                EncryptedPin = PinProtection.Protect(TokenPinBox.Password),
                RequireKskRoot = true,
                FacilityCode = ReadExistingSetting("FacilityCode", ""),
                MaxRequestBytes = 2097152
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

    private string ReadExistingSetting(string propertyName, string fallback)
    {
        try
        {
            if (!File.Exists(_settingsPath)) return fallback;
            using var document = JsonDocument.Parse(File.ReadAllText(_settingsPath));
            var server = document.RootElement.GetProperty("SigningServer");
            return server.TryGetProperty(propertyName, out var value)
                ? value.GetString() ?? fallback
                : fallback;
        }
        catch
        {
            return fallback;
        }
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
            ProcessStartInfo psi;
            if (File.Exists(exe))
            {
                psi = new ProcessStartInfo(exe) { WorkingDirectory = _serverDir, UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden };
            }
            else
            {
                var project = Path.Combine(_serverDir, "KSKSigningServer.csproj");
                if (!File.Exists(project)) throw new FileNotFoundException("Không tìm thấy KSKSigningServer.exe hoặc project server.", project);
                psi = new ProcessStartInfo("dotnet", $"run --project \"{project}\"")
                {
                    WorkingDirectory = _serverDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
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

            await WarmUpTokenSessionAsync();
            SetServerState(ServerUiState.Running, "Đang hoạt động");
            WriteLog("SUCCESS", "Signing Server đã hoạt động. PIN được quản lý và tự đăng nhập tại Server.");
            if (showSuccessDialog)
                WpfMessageBox.Show("Signing Server đã khởi động và đăng nhập USB Token thành công. Client chỉ cần kết nối để ký.", "Server sẵn sàng", MessageBoxButton.OK, MessageBoxImage.Information);
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

    private async Task WarmUpTokenSessionAsync()
    {
        if (!int.TryParse(PortText.Text, out var port))
            throw new InvalidOperationException("Cổng Server không hợp lệ.");

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-API-Key", ApiKeyBox.Password.Trim());
        var statusUrl = $"http://127.0.0.1:{port}/api/status";
        Exception? lastError = null;

        for (var attempt = 1; attempt <= 30; attempt++)
        {
            try
            {
                using var response = await client.GetAsync(statusUrl);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using var statusDoc = JsonDocument.Parse(json);
                    var activated = statusDoc.RootElement.TryGetProperty("tokenActivated", out var node) && node.GetBoolean();
                    if (activated)
                    {
                        WriteLog("INFO", "Server đã sẵn sàng và USB Token đã đăng nhập bằng PIN cấu hình tại Server.");
                        return;
                    }

                    var message = statusDoc.RootElement.TryGetProperty("pkcs11", out var pkcs11) &&
                                  pkcs11.TryGetProperty("lastError", out var errorNode)
                        ? errorNode.GetString()
                        : null;
                    lastError = new InvalidOperationException(message ?? "Server đã chạy nhưng chưa đăng nhập được USB Token.");
                }
                else
                {
                    lastError = new InvalidOperationException($"Server trả HTTP {(int)response.StatusCode}.");
                }
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
