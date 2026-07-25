using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace KSKSigningManager;

public partial class MainWindow : Window
{
    private readonly string _serverDir;
    private readonly string _settingsPath;
    private Process? _serverProcess;

    public MainWindow()
    {
        InitializeComponent();
        _serverDir = FindServerDirectory();
        _settingsPath = Path.Combine(_serverDir, "appsettings.json");
        RefreshCertificates();
        LoadSettings();
        UpdateAddress();
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

    private void RefreshCertificates_Click(object sender, RoutedEventArgs e) => RefreshCertificates();

    private void RefreshCertificates()
    {
        var items = new List<CertificateItem>();
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        foreach (var cert in store.Certificates.Cast<X509Certificate2>()
                     .Where(c => c.HasPrivateKey && c.NotAfter > DateTime.Now &&
                         (c.Issuer.Contains("MISA-CA", StringComparison.OrdinalIgnoreCase) ||
                          c.Subject.Contains("MISA-CA", StringComparison.OrdinalIgnoreCase))))
        {
            items.Add(new CertificateItem(cert));
        }
        CertificateCombo.ItemsSource = items;
        if (items.Count > 0) CertificateCombo.SelectedIndex = 0;
        else StatusText.Text = "Trạng thái: Không tìm thấy chứng thư MISA-CA có private key";
    }

    private void CertificateCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CertificateCombo.SelectedItem is CertificateItem item)
            ThumbprintText.Text = item.Thumbprint;
    }

    private void GenerateApiKey_Click(object sender, RoutedEventArgs e)
    {
        ApiKeyText.Text = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(_settingsPath)) { GenerateApiKey_Click(this, new RoutedEventArgs()); return; }
            using var doc = JsonDocument.Parse(File.ReadAllText(_settingsPath));
            var s = doc.RootElement.GetProperty("SigningServer");
            var urls = s.TryGetProperty("Urls", out var u) ? u.GetString() ?? "" : "";
            PortText.Text = new Uri(urls.Replace("0.0.0.0", "127.0.0.1")).Port.ToString();
            ApiKeyText.Text = s.TryGetProperty("ApiKey", out var a) ? a.GetString() ?? "" : "";
            var thumb = s.TryGetProperty("CertificateThumbprint", out var t) ? t.GetString() ?? "" : "";
            ThumbprintText.Text = thumb;
            var match = CertificateCombo.Items.Cast<CertificateItem>().FirstOrDefault(x => x.Thumbprint.Equals(thumb, StringComparison.OrdinalIgnoreCase));
            if (match is not null) CertificateCombo.SelectedItem = match;
            if (string.IsNullOrWhiteSpace(ApiKeyText.Text) || ApiKeyText.Text.StartsWith("CHANGE_ME"))
                GenerateApiKey_Click(this, new RoutedEventArgs());
        }
        catch (Exception ex) { StatusText.Text = "Không đọc được cấu hình: " + ex.Message; }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveConfiguration();
            StatusText.Text = "Trạng thái: Đã lưu cấu hình thành công";
            MessageBox.Show("Đã lưu cấu hình. MISA-CA có thể vẫn yêu cầu nhập PIN một lần để mở phiên Token; Server sẽ giữ phiên khóa cho các lần ký tiếp theo.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Không thể lưu", MessageBoxButton.OK, MessageBoxImage.Error); }
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

        var allowed = ResolveAllowedIps();
        var obj = new
        {
            SigningServer = new
            {
                Urls = $"http://0.0.0.0:{port}", ApiKey = ApiKeyText.Text.Trim(), AllowedIps = allowed,
                CertificateThumbprint = ThumbprintText.Text.Trim(), CertificateSubjectContains = "MISA-CA",
                StoreLocation = "CurrentUser", RequireKskRoot = true, FacilityCode = "",
                MaxRequestBytes = 2097152, EncryptedPin = encryptedPin
            }
        };
        Directory.CreateDirectory(_serverDir);
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true }));
        TryOpenFirewall(port);
        UpdateAddress();
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

    private static void TryOpenFirewall(int port)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"advfirewall firewall add rule name=\"KSK Signing Server {port}\" dir=in action=allow protocol=TCP localport={port}",
                UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden
            })?.WaitForExit(15000);
        }
        catch { }
    }

    private async void StartServer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveConfiguration();

            if (_serverProcess is not { HasExited: false })
            {
                var exe = Path.Combine(_serverDir, "KSKSigningServer.exe");
                ProcessStartInfo psi;
                if (File.Exists(exe))
                    psi = new ProcessStartInfo(exe) { WorkingDirectory = _serverDir, UseShellExecute = true };
                else
                    psi = new ProcessStartInfo("dotnet", $"run --project \"{Path.Combine(_serverDir, "KSKSigningServer.csproj")}\"")
                    { WorkingDirectory = _serverDir, UseShellExecute = false, CreateNoWindow = true };

                _serverProcess = Process.Start(psi);
            }

            StatusText.Text = "Trạng thái: Đang khởi động Server và mở phiên Token...";
            await WarmUpTokenSessionAsync();
            StatusText.Text = "Trạng thái: SERVER ĐANG HOẠT ĐỘNG - TOKEN ĐÃ MỞ PHIÊN";

            MessageBox.Show(
                "Signing Server đã khởi động và Token đã ký thử thành công.\n\n" +
                "Phiên private key đang được giữ trong bộ nhớ. Các lần ký tiếp theo sẽ dùng lại phiên này.",
                "Server sẵn sàng", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Trạng thái: Server chưa sẵn sàng ký";
            MessageBox.Show(
                "Server có thể đã chạy nhưng chưa mở được phiên Token.\n\n" + ex.Message +
                "\n\nHãy kiểm tra USB Token, chứng thư và nhập PIN trong hộp thoại MISA nếu được yêu cầu.",
                "Không mở được phiên Token", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task WarmUpTokenSessionAsync()
    {
        if (!int.TryParse(PortText.Text, out var port))
            throw new InvalidOperationException("Cổng Server không hợp lệ.");

        var apiKey = ApiKeyText.Text.Trim();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-API-Key", apiKey);

        var baseUrl = $"http://127.0.0.1:{port}";
        Exception? lastError = null;

        // Chờ ASP.NET Server thực sự lắng nghe cổng trước khi gọi warm-up.
        for (var attempt = 1; attempt <= 30; attempt++)
        {
            try
            {
                using var statusResponse = await client.GetAsync(baseUrl + "/api/status");
                if (statusResponse.IsSuccessStatusCode)
                {
                    using var warmupContent = new StringContent("{}", Encoding.UTF8, "application/json");
                    using var warmupResponse = await client.PostAsync(baseUrl + "/api/test-token", warmupContent);
                    var body = await warmupResponse.Content.ReadAsStringAsync();
                    if (!warmupResponse.IsSuccessStatusCode)
                        throw new InvalidOperationException(ReadServerMessage(body, warmupResponse.ReasonPhrase));
                    return;
                }

                lastError = new InvalidOperationException($"Server trả HTTP {(int)statusResponse.StatusCode}.");
            }
            catch (HttpRequestException ex)
            {
                lastError = ex;
            }
            catch (TaskCanceledException ex)
            {
                lastError = ex;
            }

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
            if (doc.RootElement.TryGetProperty("message", out var message))
                return message.GetString() ?? fallback ?? "Ký thử Token thất bại.";
        }
        catch { }

        return string.IsNullOrWhiteSpace(json) ? fallback ?? "Ký thử Token thất bại." : json;
    }

    private void StopServer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_serverProcess is { HasExited: false }) _serverProcess.Kill(true);
            StatusText.Text = "Trạng thái: Đã dừng Server";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message); }
    }

    private void UpdateAddress()
    {
        var ip = GetLanIp() ?? "127.0.0.1";
        AddressText.Text = $"Địa chỉ LAN: http://{ip}:{PortText.Text}   |   API Key: {ApiKeyText.Text}";
    }

    private static string? GetLanIp()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.ToString().StartsWith("169.254"))?.ToString();
    }

    private sealed class CertificateItem
    {
        public CertificateItem(X509Certificate2 cert)
        {
            Thumbprint = cert.Thumbprint ?? "";
            DisplayName = $"{cert.GetNameInfo(X509NameType.SimpleName, false)} | Hết hạn {cert.NotAfter:dd/MM/yyyy}";
        }
        public string Thumbprint { get; }
        public string DisplayName { get; }
    }
}
