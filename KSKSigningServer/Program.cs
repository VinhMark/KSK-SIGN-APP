using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Text.Json;
using System.Xml;
using Microsoft.Win32.SafeHandles;

var builder = WebApplication.CreateBuilder(args);
var options = builder.Configuration.GetSection("SigningServer").Get<SigningServerOptions>() ?? new();
builder.WebHost.UseUrls(options.Urls);
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<CertificateService>();
builder.Services.AddSingleton<TokenPinService>();
builder.Services.AddSingleton<SigningKeySession>();
builder.Services.AddSingleton<SigningQueue>();
builder.Services.AddSingleton<ServerMetrics>();
builder.Services.AddSingleton<AuditLogService>();

var app = builder.Build();

app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api"))
    {
        if (!IpAccess.IsAllowed(ctx.Connection.RemoteIpAddress, options.AllowedIps))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            await ctx.Response.WriteAsJsonAsync(new { success = false, message = "IP không được phép." });
            return;
        }

        var supplied = ctx.Request.Headers["X-API-Key"].ToString();
        if (!SecureEquals(supplied, options.ApiKey))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsJsonAsync(new { success = false, message = "API key không hợp lệ." });
            return;
        }
    }

    await next();
});

app.MapGet("/", () => Results.Text("KSK Signing Server v7 is running."));

app.MapGet("/api/status", (CertificateService certs, ServerMetrics metrics, SigningQueue queue) =>
{
    var cert = certs.FindCertificate();
    return Results.Ok(new
    {
        success = cert is not null,
        serverTime = DateTimeOffset.Now,
        version = "7.0",
        queueLength = queue.WaitingCount,
        metrics = metrics.Snapshot(),
        certificate = cert is null ? null : CertificateInfo.From(cert)
    });
});

app.MapPost("/api/test-token", async (SigningKeySession keySession) =>
{
    try
    {
        var signature = keySession.SignTest();
        return Results.Ok(new { success = true, message = "Token ký thử thành công. Phiên khóa được giữ để các lần ký sau không hỏi lại PIN.", signatureBytes = signature.Length, certificate = CertificateInfo.From(keySession.Certificate) });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { success = false, message = ex.Message });
    }
});

app.MapPost("/api/sign-xml", async (HttpContext ctx, SigningKeySession keySession,
    SigningQueue queue, ServerMetrics metrics, AuditLogService logs, CancellationToken cancellationToken) =>
{
    if (ctx.Request.ContentLength is > 0 && ctx.Request.ContentLength > options.MaxRequestBytes)
        return Results.BadRequest(new { success = false, message = "Dữ liệu vượt quá giới hạn." });

    SignXmlRequest? request;
    try
    {
        request = await JsonSerializer.DeserializeAsync<SignXmlRequest>(ctx.Request.Body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken);
    }
    catch (JsonException)
    {
        return Results.BadRequest(new { success = false, message = "JSON không hợp lệ." });
    }

    if (request is null || string.IsNullOrWhiteSpace(request.Xml))
        return Results.BadRequest(new { success = false, message = "Thiếu XML cần ký." });

    var stopwatch = Stopwatch.StartNew();
    await queue.EnterAsync(cancellationToken);
    try
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(request.Xml);
        ValidateKskDocument(doc, request, options);

        var cert = keySession.Certificate;
        XmlSigner.Sign(doc, keySession);
        var signedXml = ToUtf8Xml(doc);
        var result = new SignXmlResponse(true, "Ký thành công.", signedXml,
            Convert.ToBase64String(Encoding.UTF8.GetBytes(signedXml)), cert.Thumbprint,
            stopwatch.ElapsedMilliseconds);

        metrics.RecordSuccess(stopwatch.Elapsed);
        await logs.WriteAsync(ctx, request, true, null, cert.Thumbprint, stopwatch.ElapsedMilliseconds);
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        metrics.RecordFailure();
        await logs.WriteAsync(ctx, request, false, ex.Message, null, stopwatch.ElapsedMilliseconds);
        return Results.BadRequest(new SignXmlResponse(false, ex.Message, null, null, null, stopwatch.ElapsedMilliseconds));
    }
    finally
    {
        queue.Exit();
    }
});

app.MapPost("/api/protect-pin", (ProtectPinRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Pin))
        return Results.BadRequest(new { success = false, message = "PIN trống." });

    var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(request.Pin), null, DataProtectionScope.CurrentUser);
    return Results.Ok(new { success = true, encryptedPin = Convert.ToBase64String(bytes) });
});

app.Run();

static bool SecureEquals(string? left, string? right)
{
    var a = Encoding.UTF8.GetBytes(left ?? string.Empty);
    var b = Encoding.UTF8.GetBytes(right ?? string.Empty);
    return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
}

static void ValidateKskDocument(XmlDocument doc, SignXmlRequest request, SigningServerOptions options)
{
    var root = doc.DocumentElement ?? throw new InvalidOperationException("XML không có phần tử gốc.");
    if (options.RequireKskRoot && !string.Equals(root.Name, "root", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Chỉ cho phép ký XML nghiệp vụ KSK có phần tử gốc <root>.");

    var required = new[] { "SO", "HOTEN", "SOCMND_PASSPORT", "NGAYKETLUAN", "KETLUAN" };
    foreach (var name in required)
        if (root.SelectSingleNode(name) is null)
            throw new InvalidOperationException($"XML thiếu trường bắt buộc {name}.");

    if (!string.IsNullOrWhiteSpace(options.FacilityCode))
    {
        var facility = root.SelectSingleNode("IDBENHVIEN")?.InnerText?.Trim();
        if (!string.Equals(facility, options.FacilityCode, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Mã cơ sở trong XML không được phép ký.");
    }

    if (!string.IsNullOrWhiteSpace(request.Profile) && request.Profile is not ("KSK" or "KSK_LAI_XE"))
        throw new InvalidOperationException("SignProfile không hợp lệ.");
}

static string ToUtf8Xml(XmlDocument doc)
{
    using var stream = new MemoryStream();
    using (var writer = XmlWriter.Create(stream, new XmlWriterSettings
    {
        Encoding = new UTF8Encoding(false),
        Indent = false,
        OmitXmlDeclaration = false
    }))
    {
        doc.Save(writer);
    }
    return Encoding.UTF8.GetString(stream.ToArray());
}

public sealed class SigningServerOptions
{
    public string Urls { get; set; } = "http://0.0.0.0:7443";
    public string ApiKey { get; set; } = "";
    public string[] AllowedIps { get; set; } = ["127.0.0.1", "::1"];
    public string CertificateThumbprint { get; set; } = "";
    public string CertificateSubjectContains { get; set; } = "MISA-CA";
    public string StoreLocation { get; set; } = "CurrentUser";
    public bool RequireKskRoot { get; set; } = true;
    public string FacilityCode { get; set; } = "";
    public long MaxRequestBytes { get; set; } = 2 * 1024 * 1024;
    public string EncryptedPin { get; set; } = "";
}

public sealed record SignXmlRequest(string Xml, string? Profile, string? RecordId, string? UserName, string? MachineName);
public sealed record SignXmlResponse(bool Success, string Message, string? SignedXml, string? SignedBase64,
    string? CertificateThumbprint, long ElapsedMilliseconds);
public sealed record ProtectPinRequest(string Pin);

public sealed record CertificateInfo(string Subject, string Issuer, string Thumbprint, string SerialNumber,
    DateTime NotBefore, DateTime NotAfter, bool HasPrivateKey)
{
    public static CertificateInfo From(X509Certificate2 cert) => new(cert.Subject, cert.Issuer,
        cert.Thumbprint ?? string.Empty, cert.SerialNumber, cert.NotBefore, cert.NotAfter, cert.HasPrivateKey);
}

public sealed class CertificateService(SigningServerOptions options)
{
    public X509Certificate2? FindCertificate()
    {
        var location = string.Equals(options.StoreLocation, "LocalMachine", StringComparison.OrdinalIgnoreCase)
            ? StoreLocation.LocalMachine : StoreLocation.CurrentUser;

        using var store = new X509Store(StoreName.My, location);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        var valid = store.Certificates.Find(X509FindType.FindByTimeValid, DateTime.Now, false)
            .Cast<X509Certificate2>().Where(x => x.HasPrivateKey).ToArray();

        if (!string.IsNullOrWhiteSpace(options.CertificateThumbprint))
            return valid.FirstOrDefault(x => Normalize(x.Thumbprint) == Normalize(options.CertificateThumbprint));

        if (!string.IsNullOrWhiteSpace(options.CertificateSubjectContains))
            return valid.FirstOrDefault(x => x.Subject.Contains(options.CertificateSubjectContains, StringComparison.OrdinalIgnoreCase)
                                          || x.Issuer.Contains(options.CertificateSubjectContains, StringComparison.OrdinalIgnoreCase));
        return valid.FirstOrDefault();
    }

    private static string Normalize(string? value) => (value ?? string.Empty).Replace(" ", string.Empty).ToUpperInvariant();
}

public sealed class TokenPinService(SigningServerOptions options)
{
    public void TryApplyConfiguredPin(RSA rsa)
    {
        if (string.IsNullOrWhiteSpace(options.EncryptedPin)) return;
        if (rsa is not RSACng rsaCng) return;

        var pin = Unprotect(options.EncryptedPin);
        if (string.IsNullOrEmpty(pin)) return;

        var bytes = Encoding.Unicode.GetBytes(pin + "\0");
        var status = NativeMethods.NCryptSetProperty(rsaCng.Key.Handle, "SmartCardPin", bytes, bytes.Length, 0);
        CryptographicOperations.ZeroMemory(bytes);
        if (status != 0)
            throw new CryptographicException($"Không thể nạp PIN cho MISA-CA (NCrypt lỗi 0x{status:X8}).");
    }

    private static string Unprotect(string encrypted)
    {
        try
        {
            var protectedBytes = Convert.FromBase64String(encrypted);
            var plain = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            try { return Encoding.UTF8.GetString(plain); }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Không giải mã được PIN. Hãy tạo lại PIN bằng đúng tài khoản Windows chạy Server.", ex);
        }
    }
}

internal static class NativeMethods
{
    [DllImport("ncrypt.dll", CharSet = CharSet.Unicode)]
    internal static extern int NCryptSetProperty(SafeNCryptHandle hObject, string pszProperty,
        byte[] pbInput, int cbInput, int dwFlags);
}

public sealed class SigningKeySession : IDisposable
{
    private readonly CertificateService _certificates;
    private readonly TokenPinService _pinService;
    private readonly object _sync = new();
    private X509Certificate2? _certificate;
    private RSA? _privateKey;

    public SigningKeySession(CertificateService certificates, TokenPinService pinService)
    {
        _certificates = certificates;
        _pinService = pinService;
    }

    public X509Certificate2 Certificate
    {
        get
        {
            EnsureOpened();
            return _certificate!;
        }
    }

    public RSA PrivateKey
    {
        get
        {
            EnsureOpened();
            return _privateKey!;
        }
    }

    public byte[] SignTest()
    {
        return ExecuteWithPrivateKey(key =>
            key.SignData(Encoding.UTF8.GetBytes("KSK_TOKEN_TEST"), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    /// <summary>
    /// Thực hiện thao tác ký trong một phiên khóa dùng chung. PIN đã lưu bằng DPAPI được
    /// nạp lại trước mỗi lượt ký. Nếu middleware làm hết phiên xác thực, thao tác được thử
    /// lại đúng một lần sau khi nạp PIN lần nữa.
    /// </summary>
    public T ExecuteWithPrivateKey<T>(Func<RSA, T> operation)
    {
        lock (_sync)
        {
            EnsureOpened();
            _pinService.TryApplyConfiguredPin(_privateKey!);

            try
            {
                return operation(_privateKey!);
            }
            catch (CryptographicException firstError)
            {
                try
                {
                    _pinService.TryApplyConfiguredPin(_privateKey!);
                    return operation(_privateKey!);
                }
                catch (Exception retryError)
                {
                    throw new CryptographicException(
                        "Phiên xác thực Token đã hết hoặc MISA-CA không chấp nhận nạp lại PIN tự động. " +
                        "Server đã nạp lại PIN và thử ký lại 1 lần nhưng không thành công.",
                        new AggregateException(firstError, retryError));
                }
            }
        }
    }

    private void EnsureOpened()
    {
        if (_certificate is not null && _privateKey is not null) return;
        lock (_sync)
        {
            if (_certificate is not null && _privateKey is not null) return;
            var cert = _certificates.FindCertificate()
                ?? throw new InvalidOperationException("Không tìm thấy chứng thư MISA-CA phù hợp.");
            var key = cert.GetRSAPrivateKey()
                ?? throw new InvalidOperationException("Không lấy được RSA private key. Driver Token có thể chưa sẵn sàng.");
            _pinService.TryApplyConfiguredPin(key);
            _certificate = cert;
            _privateKey = key;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _privateKey?.Dispose();
            _certificate?.Dispose();
            _privateKey = null;
            _certificate = null;
        }
    }
}

public static class XmlSigner
{
    public static void Sign(XmlDocument doc, SigningKeySession keySession)
    {
        var certificate = keySession.Certificate;

        var signatureElement = keySession.ExecuteWithPrivateKey(privateKey =>
        {
            var signedXml = new SignedXml(doc) { SigningKey = privateKey };
            signedXml.SignedInfo!.CanonicalizationMethod = SignedXml.XmlDsigCanonicalizationUrl;
            signedXml.SignedInfo.SignatureMethod = SignedXml.XmlDsigRSASHA1Url;
            var reference = new Reference { Uri = "", DigestMethod = SignedXml.XmlDsigSHA1Url };
            reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
            signedXml.AddReference(reference);

            var keyInfo = new KeyInfo();
            keyInfo.AddClause(new RSAKeyValue(privateKey));
            keyInfo.AddClause(new KeyInfoX509Data(certificate));
            signedXml.KeyInfo = keyInfo;

            signedXml.ComputeSignature();
            return signedXml.GetXml();
        });

        doc.DocumentElement!.AppendChild(doc.ImportNode(signatureElement, true));
    }
}

public sealed class SigningQueue
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private int _waiting;
    public int WaitingCount => Math.Max(0, Volatile.Read(ref _waiting));

    public async Task EnterAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _waiting);
        try { await _semaphore.WaitAsync(cancellationToken); }
        finally { Interlocked.Decrement(ref _waiting); }
    }

    public void Exit() => _semaphore.Release();
}

public sealed class ServerMetrics
{
    private long _success;
    private long _failure;
    private long _totalMilliseconds;

    public void RecordSuccess(TimeSpan elapsed)
    {
        Interlocked.Increment(ref _success);
        Interlocked.Add(ref _totalMilliseconds, (long)elapsed.TotalMilliseconds);
    }

    public void RecordFailure() => Interlocked.Increment(ref _failure);

    public object Snapshot()
    {
        var success = Interlocked.Read(ref _success);
        var failure = Interlocked.Read(ref _failure);
        var total = Interlocked.Read(ref _totalMilliseconds);
        return new { signed = success, failed = failure, averageMilliseconds = success == 0 ? 0 : total / success };
    }
}

public static class IpAccess
{
    public static bool IsAllowed(IPAddress? ip, IEnumerable<string>? rules)
    {
        if (ip is null) return false;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();

        foreach (var rawRule in rules ?? [])
        {
            var rule = rawRule.Trim();
            if (rule == "*") return true;

            if (IPAddress.TryParse(rule, out var exact))
            {
                if (exact.IsIPv4MappedToIPv6) exact = exact.MapToIPv4();
                if (ip.Equals(exact)) return true;
                continue;
            }

            var parts = rule.Split('/', 2);
            if (parts.Length == 2 && IPAddress.TryParse(parts[0], out var network) && int.TryParse(parts[1], out var prefix))
            {
                if (network.IsIPv4MappedToIPv6) network = network.MapToIPv4();
                if (ip.AddressFamily == network.AddressFamily && InSubnet(ip, network, prefix)) return true;
            }
        }
        return false;
    }

    private static bool InSubnet(IPAddress address, IPAddress network, int prefix)
    {
        var a = address.GetAddressBytes();
        var n = network.GetAddressBytes();
        if (a.Length != n.Length || prefix < 0 || prefix > a.Length * 8) return false;

        for (var i = 0; i < a.Length; i++)
        {
            var bits = Math.Clamp(prefix - i * 8, 0, 8);
            var mask = bits == 0 ? 0 : (0xFF << (8 - bits)) & 0xFF;
            if ((a[i] & mask) != (n[i] & mask)) return false;
        }
        return true;
    }
}

public sealed class AuditLogService
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task WriteAsync(HttpContext ctx, SignXmlRequest? request, bool success, string? error,
        string? thumbprint, long elapsedMilliseconds)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(folder);
        var line = JsonSerializer.Serialize(new
        {
            time = DateTimeOffset.Now,
            remoteIp = ctx.Connection.RemoteIpAddress?.ToString(),
            request?.Profile,
            request?.RecordId,
            request?.UserName,
            request?.MachineName,
            success,
            error,
            thumbprint,
            elapsedMilliseconds
        });

        await _gate.WaitAsync();
        try
        {
            await File.AppendAllTextAsync(Path.Combine(folder, $"sign-{DateTime.Now:yyyyMMdd}.jsonl"),
                line + Environment.NewLine);
        }
        finally
        {
            _gate.Release();
        }
    }
}
