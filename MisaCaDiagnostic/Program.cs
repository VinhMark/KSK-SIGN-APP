using Microsoft.Win32;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine("MISA-CA Diagnostic Tool v1.0");
Console.WriteLine(new string('=', 60));

var report = new DiagnosticReport
{
    ComputerName = Environment.MachineName,
    UserName = Environment.UserName,
    OsVersion = Environment.OSVersion.ToString(),
    GeneratedAt = DateTimeOffset.Now
};

foreach (var location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
{
    try
    {
        using var store = new X509Store(StoreName.My, location);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        foreach (var cert in store.Certificates.Cast<X509Certificate2>().Where(c => c.HasPrivateKey))
        {
            var item = new CertificateInfo
            {
                StoreLocation = location.ToString(), Subject = cert.Subject, Issuer = cert.Issuer,
                Thumbprint = cert.Thumbprint, SerialNumber = cert.SerialNumber,
                NotBefore = cert.NotBefore, NotAfter = cert.NotAfter, HasPrivateKey = cert.HasPrivateKey
            };
            try
            {
                using var rsa = cert.GetRSAPrivateKey();
                item.Algorithm = rsa?.GetType().FullName;
                if (rsa is RSACng cng)
                {
                    var providerName = cng.Key.Provider?.Provider;
                    item.Provider = providerName;
                    item.KeyName = cng.Key.KeyName;

                    // CngKey không có thuộc tính IsHardwareDevice trên mọi phiên bản .NET.
                    // Suy luận an toàn dựa trên tên KSP/provider thường dùng cho smart card/token.
                    item.IsHardwareDevice = IsLikelyHardwareProvider(providerName);
                }
                else if (rsa is RSACryptoServiceProvider csp)
                {
                    item.Provider = csp.CspKeyContainerInfo.ProviderName;
                    item.KeyName = csp.CspKeyContainerInfo.KeyContainerName;
                    item.IsHardwareDevice = csp.CspKeyContainerInfo.HardwareDevice;
                }
            }
            catch (Exception ex) { item.PrivateKeyError = ex.Message; }
            report.Certificates.Add(item);
        }
    }
    catch (Exception ex) { report.Errors.Add($"Đọc store {location}: {ex.Message}"); }
}

var roots = new[]
{
    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32")
}.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
foreach (var root in roots)
{
    try
    {
        foreach (var file in Directory.EnumerateFiles(root, "*.dll", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file).ToLowerInvariant();
            var path = file.ToLowerInvariant();
            if (name.Contains("pkcs11") || name.Contains("cryptoki") || (path.Contains("misa") && (name.Contains("token") || name.Contains("ca"))))
                report.PossiblePkcs11Libraries.Add(file);
            if (report.PossiblePkcs11Libraries.Count >= 100) break;
        }
    }
    catch (Exception ex) { report.Errors.Add($"Quét {root}: {ex.Message}"); }
}

foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
{
    try
    {
        using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography\Defaults\Provider");
        if (key is null) continue;
        report.CryptoProviders.AddRange(key.GetSubKeyNames().Where(n => n.Contains("MISA", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Token", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Smart Card", StringComparison.OrdinalIgnoreCase)));
    }
    catch (Exception ex) { report.Errors.Add($"Đọc provider registry: {ex.Message}"); }
}

var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
var output = Path.Combine(AppContext.BaseDirectory, $"MISA_CA_Diagnostic_{DateTime.Now:yyyyMMdd_HHmmss}.json");
File.WriteAllText(output, json, new UTF8Encoding(false));

Console.WriteLine($"Chứng thư có private key: {report.Certificates.Count}");
foreach (var c in report.Certificates)
{
    Console.WriteLine($"- [{c.StoreLocation}] {c.Subject}");
    Console.WriteLine($"  Provider: {c.Provider ?? "(không xác định)"}");
    Console.WriteLine($"  Hardware: {c.IsHardwareDevice?.ToString() ?? "?"}");
    Console.WriteLine($"  Thumbprint: {c.Thumbprint}");
}
Console.WriteLine($"DLL PKCS#11/Token khả nghi: {report.PossiblePkcs11Libraries.Count}");
foreach (var f in report.PossiblePkcs11Libraries.Take(20)) Console.WriteLine($"- {f}");
Console.WriteLine($"\nĐã xuất báo cáo: {output}");
Console.WriteLine("Nhấn Enter để đóng.");
Console.ReadLine();


static bool? IsLikelyHardwareProvider(string? providerName)
{
    if (string.IsNullOrWhiteSpace(providerName)) return null;

    var value = providerName.Trim();
    if (value.Contains("Smart Card", StringComparison.OrdinalIgnoreCase)
        || value.Contains("Token", StringComparison.OrdinalIgnoreCase)
        || value.Contains("MISA", StringComparison.OrdinalIgnoreCase)
        || value.Contains("eToken", StringComparison.OrdinalIgnoreCase)
        || value.Contains("SafeNet", StringComparison.OrdinalIgnoreCase))
        return true;

    if (value.Contains("Software", StringComparison.OrdinalIgnoreCase)
        || value.Equals("Microsoft Software Key Storage Provider", StringComparison.OrdinalIgnoreCase))
        return false;

    return null;
}

public sealed class DiagnosticReport
{
    public string ComputerName { get; set; } = "";
    public string UserName { get; set; } = "";
    public string OsVersion { get; set; } = "";
    public DateTimeOffset GeneratedAt { get; set; }
    public List<CertificateInfo> Certificates { get; } = [];
    public List<string> PossiblePkcs11Libraries { get; } = [];
    public List<string> CryptoProviders { get; } = [];
    public List<string> Errors { get; } = [];
}
public sealed class CertificateInfo
{
    public string StoreLocation { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Issuer { get; set; } = "";
    public string Thumbprint { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public DateTime NotBefore { get; set; }
    public DateTime NotAfter { get; set; }
    public bool HasPrivateKey { get; set; }
    public string? Algorithm { get; set; }
    public string? Provider { get; set; }
    public string? KeyName { get; set; }
    public bool? IsHardwareDevice { get; set; }
    public string? PrivateKeyError { get; set; }
}
