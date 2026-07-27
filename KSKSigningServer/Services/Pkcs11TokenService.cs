using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Net.Pkcs11Interop.Common;
using Net.Pkcs11Interop.HighLevelAPI;
using Pkcs11Session = Net.Pkcs11Interop.HighLevelAPI.ISession;

namespace KSKSigningServer.Services;

public sealed class Pkcs11TokenService : IDisposable
{
    private readonly SigningServerOptions _options;
    private readonly ILogger<Pkcs11TokenService> _logger;
    private readonly object _sync = new();
    private readonly Pkcs11InteropFactories _factories = new();

    private IPkcs11Library? _library;
    private ISlot? _slot;
    private Pkcs11Session? _session;
    private IObjectHandle? _privateKey;
    private X509Certificate2? _certificate;
    private string? _lastError;

    public Pkcs11TokenService(
        SigningServerOptions options,
        ILogger<Pkcs11TokenService> logger)
    {
        _options = options;
        _logger = logger;
    }

    public bool IsLoggedIn
    {
        get
        {
            lock (_sync)
                return _session is not null &&
                       _privateKey is not null &&
                       _certificate is not null;
        }
    }

    public X509Certificate2 Certificate
    {
        get
        {
            lock (_sync)
                return _certificate
                       ?? throw new Pkcs11TokenException(
                           "TOKEN_SESSION_EXPIRED",
                           "Phiên Token chưa được đăng nhập.");
        }
    }

    public TokenStatus GetStatus()
    {
        lock (_sync)
        {
            try
            {
                EnsureLibrary();
                var slot = SelectSlot();
                var info = slot.GetTokenInfo();

                X509Certificate2? cert = _certificate;
                if (cert is null)
                {
                    try
                    {
                        using var probe = slot.OpenSession(SessionType.ReadOnly);
                        cert = FindCertificate(probe);
                    }
                    catch
                    {
                        // Một số middleware không cho đọc chứng thư trước Login.
                    }
                }

                return new TokenStatus(
                    LibraryLoaded: true,
                    TokenPresent: true,
                    TokenLabel: info.Label?.Trim(),
                    TokenSerial: info.SerialNumber?.Trim(),
                    SessionLoggedIn: IsLoggedIn,
                    Certificate: cert is null ? null : TokenCertificateInfo.From(cert),
                    LastError: _lastError);
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                return new TokenStatus(
                    LibraryLoaded: _library is not null,
                    TokenPresent: false,
                    TokenLabel: null,
                    TokenSerial: null,
                    SessionLoggedIn: false,
                    Certificate: null,
                    LastError: ex.Message);
            }
        }
    }

    public void Login(string pin)
    {
        lock (_sync)
        {
            LogoutInternal();

            try
            {
                EnsureLibrary();
                _slot = SelectSlot();
                _session = _slot.OpenSession(SessionType.ReadWrite);

                try
                {
                    _session.Login(CKU.CKU_USER, pin);
                }
                catch (Pkcs11Exception ex) when (ex.RV == CKR.CKR_USER_ALREADY_LOGGED_IN)
                {
                    // Middleware đã có phiên đăng nhập hợp lệ.
                }

                _certificate = FindCertificate(_session)
                    ?? throw new Pkcs11TokenException(
                        "TOKEN_CERTIFICATE_NOT_FOUND",
                        "Không tìm thấy chứng thư phù hợp trong USB Token.");

                _privateKey = FindPrivateKey(_session, _certificate)
                    ?? throw new Pkcs11TokenException(
                        "TOKEN_PRIVATE_KEY_NOT_FOUND",
                        "Không tìm thấy private key tương ứng trong USB Token.");

                _lastError = null;
                _logger.LogInformation(
                    "Đã Login PKCS#11. Subject={Subject}; Thumbprint={Thumbprint}",
                    _certificate.Subject,
                    _certificate.Thumbprint);
            }
            catch (Pkcs11Exception ex) when (
                ex.RV is CKR.CKR_PIN_INCORRECT or
                         CKR.CKR_PIN_INVALID or
                         CKR.CKR_PIN_LEN_RANGE)
            {
                LogoutInternal();
                throw new Pkcs11TokenException(
                    "TOKEN_PIN_INVALID",
                    "PIN USB Token không đúng.",
                    ex);
            }
            catch (Pkcs11Exception ex) when (ex.RV == CKR.CKR_PIN_LOCKED)
            {
                LogoutInternal();
                throw new Pkcs11TokenException(
                    "TOKEN_PIN_LOCKED",
                    "USB Token đã khóa PIN.",
                    ex);
            }
            catch (Pkcs11TokenException)
            {
                LogoutInternal();
                throw;
            }
            catch (Exception ex)
            {
                LogoutInternal();
                throw MapException(ex);
            }
        }
    }

    public byte[] SignSha256(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return SignHash(hash, HashAlgorithmName.SHA256);
    }

    public byte[] SignHash(
        byte[] hash,
        HashAlgorithmName hashAlgorithm)
    {
        lock (_sync)
        {
            if (_session is null ||
                _privateKey is null ||
                _certificate is null)
                throw new Pkcs11TokenException(
                    "TOKEN_SESSION_EXPIRED",
                    "Phiên Token chưa đăng nhập hoặc đã hết hạn.");

            try
            {
                byte[] digestInfo = hashAlgorithm == HashAlgorithmName.SHA256
                    ? BuildSha256DigestInfo(hash)
                    : throw new NotSupportedException(
                        $"Chưa hỗ trợ thuật toán {hashAlgorithm.Name}.");

                var mechanism =
                    _factories.MechanismFactory.Create(CKM.CKM_RSA_PKCS);

                return _session.Sign(
                    mechanism,
                    _privateKey,
                    digestInfo);
            }
            catch (Pkcs11Exception ex) when (
                ex.RV is CKR.CKR_USER_NOT_LOGGED_IN or
                         CKR.CKR_SESSION_HANDLE_INVALID or
                         CKR.CKR_SESSION_CLOSED or
                         CKR.CKR_DEVICE_REMOVED or
                         CKR.CKR_TOKEN_NOT_PRESENT)
            {
                LogoutInternal();
                throw new Pkcs11TokenException(
                    "TOKEN_SESSION_EXPIRED",
                    "Phiên Token đã hết hạn hoặc Token đã bị rút.",
                    ex);
            }
            catch (Pkcs11Exception ex)
            {
                throw new Pkcs11TokenException(
                    "SIGN_FAILED",
                    $"PKCS#11 ký thất bại: {ex.RV}.",
                    ex);
            }
        }
    }

    public void Logout()
    {
        lock (_sync)
            LogoutInternal();
    }

    private void EnsureLibrary()
    {
        if (_library is not null)
            return;

        if (string.IsNullOrWhiteSpace(_options.Pkcs11LibraryPath) ||
            !File.Exists(_options.Pkcs11LibraryPath))
            throw new Pkcs11TokenException(
                "PKCS11_LIBRARY_NOT_FOUND",
                $"Không tìm thấy PKCS#11 module: {_options.Pkcs11LibraryPath}");

        try
        {
            _library =
                _factories.Pkcs11LibraryFactory.LoadPkcs11Library(
                    _factories,
                    _options.Pkcs11LibraryPath,
                    AppType.MultiThreaded);
        }
        catch (Exception ex)
        {
            throw new Pkcs11TokenException(
                "PKCS11_LIBRARY_ERROR",
                "Không nạp được PKCS#11 module. Hãy kiểm tra đúng kiến trúc x64/x86.",
                ex);
        }
    }

    private ISlot SelectSlot()
    {
        var slots = _library!
            .GetSlotList(SlotsType.WithTokenPresent);

        foreach (var slot in slots)
        {
            var info = slot.GetTokenInfo();
            var label = info.Label?.Trim() ?? "";
            var serial = info.SerialNumber?.Trim() ?? "";

            if (!string.IsNullOrWhiteSpace(_options.TokenLabelContains) &&
                !label.Contains(
                    _options.TokenLabelContains,
                    StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.IsNullOrWhiteSpace(_options.TokenSerial) &&
                !string.Equals(
                    serial,
                    _options.TokenSerial.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                continue;

            return slot;
        }

        throw new Pkcs11TokenException(
            "TOKEN_NOT_FOUND",
            "Không tìm thấy USB Token phù hợp.");
    }

    private X509Certificate2? FindCertificate(Pkcs11Session session)
    {
        var attrs = new List<IObjectAttribute>
        {
            _factories.ObjectAttributeFactory.Create(
                CKA.CKA_CLASS, CKO.CKO_CERTIFICATE),
            _factories.ObjectAttributeFactory.Create(
                CKA.CKA_CERTIFICATE_TYPE, CKC.CKC_X_509)
        };

        var objects = session.FindAllObjects(attrs);
        X509Certificate2? fallback = null;

        foreach (var handle in objects)
        {
            var values = session.GetAttributeValue(
                handle,
                new List<CKA> { CKA.CKA_VALUE });

            var der = values[0].GetValueAsByteArray();
            if (der is null || der.Length == 0)
                continue;

            X509Certificate2 cert;
            try
            {
                cert = new X509Certificate2(der);
            }
            catch
            {
                continue;
            }

            fallback ??= cert;

            if (!string.IsNullOrWhiteSpace(_options.CertificateThumbprint) &&
                Normalize(cert.Thumbprint) ==
                Normalize(_options.CertificateThumbprint))
                return cert;

            if (!string.IsNullOrWhiteSpace(
                    _options.CertificateSubjectContains) &&
                (cert.Subject.Contains(
                     _options.CertificateSubjectContains,
                     StringComparison.OrdinalIgnoreCase) ||
                 cert.Issuer.Contains(
                     _options.CertificateSubjectContains,
                     StringComparison.OrdinalIgnoreCase)))
                return cert;
        }

        return string.IsNullOrWhiteSpace(_options.CertificateThumbprint) &&
               string.IsNullOrWhiteSpace(_options.CertificateSubjectContains)
            ? fallback
            : null;
    }

    private IObjectHandle? FindPrivateKey(
        Pkcs11Session session,
        X509Certificate2 certificate)
    {
        // Ưu tiên ghép certificate/private key theo CKA_ID.
        var certObjects = session.FindAllObjects(
            new List<IObjectAttribute>
            {
                _factories.ObjectAttributeFactory.Create(
                    CKA.CKA_CLASS, CKO.CKO_CERTIFICATE)
            });

        byte[]? certificateId = null;

        foreach (var certHandle in certObjects)
        {
            var attrs = session.GetAttributeValue(
                certHandle,
                new List<CKA> { CKA.CKA_VALUE, CKA.CKA_ID });

            var der = attrs[0].GetValueAsByteArray();
            if (der is null)
                continue;

            try
            {
                using var cert = new X509Certificate2(der);
                if (Normalize(cert.Thumbprint) ==
                    Normalize(certificate.Thumbprint))
                {
                    certificateId = attrs[1].GetValueAsByteArray();
                    break;
                }
            }
            catch
            {
                // Bỏ qua object không phải X509 hợp lệ.
            }
        }

        var keyTemplate = new List<IObjectAttribute>
        {
            _factories.ObjectAttributeFactory.Create(
                CKA.CKA_CLASS, CKO.CKO_PRIVATE_KEY),
            _factories.ObjectAttributeFactory.Create(
                CKA.CKA_KEY_TYPE, CKK.CKK_RSA),
            _factories.ObjectAttributeFactory.Create(
                CKA.CKA_SIGN, true)
        };

        if (certificateId is { Length: > 0 })
            keyTemplate.Add(
                _factories.ObjectAttributeFactory.Create(
                    CKA.CKA_ID, certificateId));

        return session.FindAllObjects(keyTemplate).FirstOrDefault();
    }

    private static byte[] BuildSha256DigestInfo(byte[] hash)
    {
        if (hash.Length != 32)
            throw new ArgumentException(
                "SHA-256 hash phải dài 32 byte.",
                nameof(hash));

        // DER DigestInfo prefix cho SHA-256:
        // SEQUENCE { AlgorithmIdentifier(sha256, NULL), OCTET STRING(hash) }
        byte[] prefix =
        [
            0x30, 0x31,
            0x30, 0x0D,
            0x06, 0x09,
            0x60, 0x86, 0x48, 0x01, 0x65,
            0x03, 0x04, 0x02, 0x01,
            0x05, 0x00,
            0x04, 0x20
        ];

        var result = new byte[prefix.Length + hash.Length];
        Buffer.BlockCopy(prefix, 0, result, 0, prefix.Length);
        Buffer.BlockCopy(hash, 0, result, prefix.Length, hash.Length);
        return result;
    }

    private void LogoutInternal()
    {
        if (_session is not null)
        {
            try { _session.Logout(); } catch { }
            try { _session.Dispose(); } catch { }
        }

        _session = null;
        _slot = null;
        _privateKey = null;

        if (_certificate is not null)
        {
            _certificate.Dispose();
            _certificate = null;
        }
    }

    private Pkcs11TokenException MapException(Exception ex)
    {
        if (ex is Pkcs11TokenException known)
            return known;

        if (ex is Pkcs11Exception pkcs11)
        {
            return pkcs11.RV switch
            {
                CKR.CKR_TOKEN_NOT_PRESENT or
                CKR.CKR_DEVICE_REMOVED
                    => new Pkcs11TokenException(
                        "TOKEN_NOT_FOUND",
                        "Không tìm thấy USB Token hoặc Token đã bị rút.",
                        ex),

                _ => new Pkcs11TokenException(
                    "TOKEN_LOGIN_FAILED",
                    $"Không đăng nhập được Token: {pkcs11.RV}.",
                    ex)
            };
        }

        return new Pkcs11TokenException(
            "TOKEN_LOGIN_FAILED",
            ex.Message,
            ex);
    }

    private static string Normalize(string? value) =>
        (value ?? "")
        .Replace(" ", "")
        .ToUpperInvariant();

    public void Dispose()
    {
        lock (_sync)
        {
            LogoutInternal();
            _library?.Dispose();
            _library = null;
        }
    }
}

public sealed record TokenStatus(
    bool LibraryLoaded,
    bool TokenPresent,
    string? TokenLabel,
    string? TokenSerial,
    bool SessionLoggedIn,
    TokenCertificateInfo? Certificate,
    string? LastError);

public sealed record TokenCertificateInfo(
    string Subject,
    string Issuer,
    string Thumbprint,
    string SerialNumber,
    DateTime NotBefore,
    DateTime NotAfter)
{
    public static TokenCertificateInfo From(X509Certificate2 cert) =>
        new(
            cert.Subject,
            cert.Issuer,
            cert.Thumbprint ?? "",
            cert.SerialNumber,
            cert.NotBefore,
            cert.NotAfter);
}

public sealed class Pkcs11TokenException : Exception
{
    public string Code { get; }

    public Pkcs11TokenException(
        string code,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }
}
