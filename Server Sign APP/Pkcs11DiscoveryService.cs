using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Net.Pkcs11Interop.Common;
using Net.Pkcs11Interop.HighLevelAPI;

namespace KSKSigningManager;

public sealed class Pkcs11DiscoveryService
{
    private readonly Pkcs11InteropFactories _factories = new();

    public IReadOnlyList<Pkcs11TokenItem> GetTokens(string libraryPath)
    {
        ValidateLibraryPath(libraryPath);

        try
        {
            using var library = _factories.Pkcs11LibraryFactory.LoadPkcs11Library(
                _factories,
                libraryPath,
                AppType.MultiThreaded);

            return library.GetSlotList(SlotsType.WithTokenPresent)
                .Select(slot =>
                {
                    var info = slot.GetTokenInfo();
                    return new Pkcs11TokenItem(
                        SlotId: slot.SlotId,
                        Label: Clean(info.Label),
                        Serial: Clean(info.SerialNumber),
                        Manufacturer: Clean(info.ManufacturerId),
                        Model: Clean(info.Model));
                })
                .OrderBy(x => x.Label)
                .ThenBy(x => x.Serial)
                .ToArray();
        }
        catch (BadImageFormatException ex)
        {
            throw new InvalidOperationException(
                "Không thể nạp DLL PKCS#11 vì sai kiến trúc x86/x64. Server hiện được build x64.", ex);
        }
        catch (DllNotFoundException ex)
        {
            throw new InvalidOperationException("Không tìm thấy hoặc không nạp được DLL PKCS#11.", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Không thể nạp thư viện PKCS#11: " + ex.Message, ex);
        }
    }

    public IReadOnlyList<Pkcs11CertificateItem> GetCertificates(string libraryPath, string tokenSerial, ulong? slotId = null)
    {
        ValidateLibraryPath(libraryPath);

        try
        {
            using var library = _factories.Pkcs11LibraryFactory.LoadPkcs11Library(
                _factories,
                libraryPath,
                AppType.MultiThreaded);

            var slot = library.GetSlotList(SlotsType.WithTokenPresent)
                .FirstOrDefault(candidate =>
                {
                    var info = candidate.GetTokenInfo();
                    var serialMatches = string.Equals(Clean(info.SerialNumber), tokenSerial?.Trim(), StringComparison.OrdinalIgnoreCase);
                    return serialMatches && (!slotId.HasValue || candidate.SlotId == slotId.Value);
                }) ?? throw new InvalidOperationException("Không tìm thấy Token đã chọn. Hãy cắm lại Token và quét lại.");

            using var session = slot.OpenSession(SessionType.ReadOnly);
            var template = new List<IObjectAttribute>
            {
                _factories.ObjectAttributeFactory.Create(CKA.CKA_CLASS, CKO.CKO_CERTIFICATE),
                _factories.ObjectAttributeFactory.Create(CKA.CKA_CERTIFICATE_TYPE, CKC.CKC_X_509)
            };

            var result = new List<Pkcs11CertificateItem>();
            foreach (var handle in session.FindAllObjects(template))
            {
                var attrs = session.GetAttributeValue(handle, new List<CKA> { CKA.CKA_VALUE, CKA.CKA_ID, CKA.CKA_LABEL });
                var der = attrs[0].GetValueAsByteArray();
                if (der is null || der.Length == 0) continue;

                try
                {
                    using var cert = new X509Certificate2(der);
                    var id = attrs[1].GetValueAsByteArray();
                    var label = attrs[2].GetValueAsString();
                    result.Add(new Pkcs11CertificateItem(
                        Thumbprint: Normalize(cert.Thumbprint),
                        CertificateIdHex: id is { Length: > 0 } ? Convert.ToHexString(id) : "",
                        Label: Clean(label),
                        Subject: cert.GetNameInfo(X509NameType.SimpleName, false),
                        FullSubject: cert.Subject,
                        Issuer: cert.Issuer,
                        SerialNumber: cert.SerialNumber,
                        NotBefore: cert.NotBefore,
                        NotAfter: cert.NotAfter));
                }
                catch (CryptographicException)
                {
                    // Bỏ qua đối tượng certificate không đọc được.
                }
            }

            return result
                .OrderByDescending(x => x.NotAfter)
                .ThenBy(x => x.Subject)
                .ToArray();
        }
        catch (BadImageFormatException ex)
        {
            throw new InvalidOperationException(
                "Không thể nạp DLL PKCS#11 vì sai kiến trúc x86/x64. Server hiện được build x64.", ex);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException("Không thể đọc chứng thư trong Token: " + ex.Message, ex);
        }
    }

    private static void ValidateLibraryPath(string libraryPath)
    {
        if (string.IsNullOrWhiteSpace(libraryPath))
            throw new InvalidOperationException("Chưa chọn thư viện PKCS#11 DLL.");
        if (!File.Exists(libraryPath))
            throw new FileNotFoundException("Không tìm thấy thư viện PKCS#11 DLL.", libraryPath);
        if (!string.Equals(Path.GetExtension(libraryPath), ".dll", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Tệp được chọn phải là thư viện .dll.");
    }

    private static string Clean(string? value) => value?.Trim('\0', ' ') ?? "";
    private static string Normalize(string? value) => (value ?? "").Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
}

public sealed record Pkcs11TokenItem(ulong SlotId, string Label, string Serial, string Manufacturer, string Model)
{
    public string DisplayName => $"{(string.IsNullOrWhiteSpace(Label) ? "Token" : Label)} | Serial: {(string.IsNullOrWhiteSpace(Serial) ? "N/A" : Serial)}";
}

public sealed record Pkcs11CertificateItem(
    string Thumbprint,
    string CertificateIdHex,
    string Label,
    string Subject,
    string FullSubject,
    string Issuer,
    string SerialNumber,
    DateTime NotBefore,
    DateTime NotAfter)
{
    public bool IsExpired => NotAfter <= DateTime.Now;
    public bool IsNotYetValid => NotBefore > DateTime.Now;
    public string DisplayName => $"{Subject} | Hết hạn {NotAfter:dd/MM/yyyy}" + (IsExpired ? " | ĐÃ HẾT HẠN" : "");
}
