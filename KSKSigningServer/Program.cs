using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using KSKSigningServer.Services;

var builder = WebApplication.CreateBuilder(args);
var options = builder.Configuration.GetSection("SigningServer").Get<SigningServerOptions>() ?? new();

builder.WebHost.UseUrls(options.Urls);
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<Pkcs11TokenService>();
builder.Services.AddSingleton<ConfiguredPinLoginService>();
builder.Services.AddSingleton<PinAttemptGuard>();
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
            await ctx.Response.WriteAsJsonAsync(ApiError("IP_NOT_ALLOWED", "IP không được phép."));
            return;
        }

        var supplied = ctx.Request.Headers["X-API-Key"].ToString();
        if (!SecureEquals(supplied, options.ApiKey))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsJsonAsync(ApiError("API_KEY_INVALID", "API Key không hợp lệ."));
            return;
        }
    }

    await next();
});

app.MapGet("/", () => Results.Text("KSK Signing Server v9 PKCS#11 Native is running."));

app.MapGet("/api/status", (Pkcs11TokenService token, ConfiguredPinLoginService pinLogin, SigningQueue queue, ServerMetrics metrics) =>
{
    string? loginCode = null;
    string? loginError = null;
    try
    {
        pinLogin.EnsureLoggedIn();
    }
    catch (Pkcs11TokenException ex)
    {
        loginCode = ex.Code;
        loginError = ex.Message;
    }

    var status = token.GetStatus();
    return Results.Ok(new
    {
        success = status.LibraryLoaded && status.TokenPresent && status.SessionLoggedIn,
        code = status.SessionLoggedIn ? "TOKEN_OK" :
               loginCode ??
               (status.TokenPresent ? "TOKEN_SESSION_EXPIRED" :
                status.LibraryLoaded ? "TOKEN_NOT_FOUND" : "PKCS11_LIBRARY_ERROR"),
        message = status.SessionLoggedIn
            ? "Server và USB Token đã sẵn sàng."
            : loginError ?? status.LastError,
        version = "11.0-server-pin",
        serverTime = DateTimeOffset.Now,
        queueLength = queue.WaitingCount,
        metrics = metrics.Snapshot(),
        pkcs11 = new
        {
            libraryPath = options.Pkcs11LibraryPath,
            libraryLoaded = status.LibraryLoaded,
            tokenPresent = status.TokenPresent,
            tokenLabel = status.TokenLabel,
            tokenSerial = status.TokenSerial,
            sessionLoggedIn = status.SessionLoggedIn,
            lastError = loginError ?? status.LastError
        },
        tokenActivated = status.SessionLoggedIn,
        pinConfigured = pinLogin.IsConfigured,
        certificate = status.Certificate
    });
});

// PIN được cấu hình và mã hóa tại Server. Client không có endpoint nhập PIN.

app.MapPost("/api/logout-token", (Pkcs11TokenService token) =>
{
    token.Logout();
    return Results.Ok(new
    {
        success = true,
        code = "TOKEN_SESSION_EXPIRED",
        message = "Đã đóng phiên đăng nhập Token.",
        tokenActivated = false
    });
});

app.MapPost("/api/test-token", (Pkcs11TokenService token, ConfiguredPinLoginService pinLogin) =>
{
    try
    {
        pinLogin.EnsureLoggedIn();
        var signature = token.SignSha256(Encoding.UTF8.GetBytes("KSK_SIGN_TEST_V9"));
        return Results.Ok(new
        {
            success = true,
            code = "SIGN_SUCCESS",
            message = "Token ký thử thành công.",
            signatureBytes = signature.Length,
            certificate = token.GetStatus().Certificate
        });
    }
    catch (Pkcs11TokenException ex)
    {
        return Results.Json(ApiError(ex.Code, ex.Message),
            statusCode: ex.Code == "TOKEN_SESSION_EXPIRED"
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status400BadRequest);
    }
});

app.MapPost("/api/sign-xml", async (
    HttpContext ctx,
    Pkcs11TokenService token,
    ConfiguredPinLoginService pinLogin,
    SigningQueue queue,
    ServerMetrics metrics,
    AuditLogService logs,
    CancellationToken cancellationToken) =>
{
    if (ctx.Request.ContentLength is > 0 &&
        ctx.Request.ContentLength > options.MaxRequestBytes)
        return Results.BadRequest(ApiError("REQUEST_TOO_LARGE", "Dữ liệu vượt quá giới hạn."));

    SignXmlRequest? request;
    try
    {
        request = await JsonSerializer.DeserializeAsync<SignXmlRequest>(
            ctx.Request.Body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken);
    }
    catch (JsonException)
    {
        return Results.BadRequest(ApiError("JSON_INVALID", "JSON không hợp lệ."));
    }

    if (request is null || string.IsNullOrWhiteSpace(request.Xml))
        return Results.BadRequest(ApiError("XML_EMPTY", "Thiếu XML cần ký."));

    try
    {
        pinLogin.EnsureLoggedIn();
    }
    catch (Pkcs11TokenException ex)
    {
        return Results.Json(
            ApiError(ex.Code, ex.Message),
            statusCode: StatusCodes.Status409Conflict);
    }

    var stopwatch = Stopwatch.StartNew();
    await queue.EnterAsync(cancellationToken);

    try
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(request.Xml);
        ValidateKskDocument(doc, request, options);

        Pkcs11XmlSigner.Sign(doc, token);
        var signedXml = ToUtf8Xml(doc);
        var cert = token.Certificate;

        metrics.RecordSuccess(stopwatch.Elapsed);
        await logs.WriteAsync(
            ctx, request, true, null, cert.Thumbprint,
            stopwatch.ElapsedMilliseconds);

        return Results.Ok(new
        {
            success = true,
            code = "SIGN_SUCCESS",
            message = "Ký thành công.",
            signedXml,
            signedBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(signedXml)),
            certificateThumbprint = cert.Thumbprint,
            elapsedMilliseconds = stopwatch.ElapsedMilliseconds
        });
    }
    catch (Pkcs11TokenException ex)
    {
        metrics.RecordFailure();
        await logs.WriteAsync(
            ctx, request, false, ex.Message, null,
            stopwatch.ElapsedMilliseconds);

        var httpCode = ex.Code == "TOKEN_SESSION_EXPIRED"
            ? StatusCodes.Status409Conflict
            : StatusCodes.Status400BadRequest;

        return Results.Json(
            new
            {
                success = false,
                code = ex.Code,
                message = ex.Message,
                tokenActivated = token.IsLoggedIn,
                elapsedMilliseconds = stopwatch.ElapsedMilliseconds
            },
            statusCode: httpCode);
    }
    catch (Exception ex)
    {
        metrics.RecordFailure();
        await logs.WriteAsync(
            ctx, request, false, ex.Message, null,
            stopwatch.ElapsedMilliseconds);

        return Results.BadRequest(new
        {
            success = false,
            code = "SIGN_FAILED",
            message = ex.Message,
            elapsedMilliseconds = stopwatch.ElapsedMilliseconds
        });
    }
    finally
    {
        queue.Exit();
    }
});

app.Lifetime.ApplicationStopping.Register(() =>
{
    var token = app.Services.GetRequiredService<Pkcs11TokenService>();
    token.Dispose();
});

app.Run();

static object ApiError(string code, string message) =>
    new { success = false, code, message };

static bool SecureEquals(string? left, string? right)
{
    var a = Encoding.UTF8.GetBytes(left ?? string.Empty);
    var b = Encoding.UTF8.GetBytes(right ?? string.Empty);
    return a.Length == b.Length &&
           CryptographicOperations.FixedTimeEquals(a, b);
}

static void ValidateKskDocument(
    XmlDocument doc,
    SignXmlRequest request,
    SigningServerOptions options)
{
    var root = doc.DocumentElement
               ?? throw new InvalidOperationException("XML không có phần tử gốc.");

    if (options.RequireKskRoot &&
        !string.Equals(root.Name, "root", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException(
            "Chỉ cho phép ký XML nghiệp vụ KSK có phần tử gốc <root>.");

    string[] required =
        ["SO", "HOTEN", "SOCMND_PASSPORT", "NGAYKETLUAN", "KETLUAN"];

    foreach (var name in required)
        if (root.SelectSingleNode(name) is null)
            throw new InvalidOperationException(
                $"XML thiếu trường bắt buộc {name}.");

    if (!string.IsNullOrWhiteSpace(options.FacilityCode))
    {
        var facility = root.SelectSingleNode("IDBENHVIEN")?.InnerText?.Trim();
        if (!string.Equals(
                facility,
                options.FacilityCode,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Mã cơ sở trong XML không được phép ký.");
    }

    if (!string.IsNullOrWhiteSpace(request.Profile) &&
        request.Profile is not ("KSK" or "KSK_LAI_XE"))
        throw new InvalidOperationException("SignProfile không hợp lệ.");
}

static string ToUtf8Xml(XmlDocument doc)
{
    using var stream = new MemoryStream();
    using (var writer = XmlWriter.Create(
               stream,
               new XmlWriterSettings
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
    public string Pkcs11LibraryPath { get; set; } =
        @"C:\Windows\System32\misaca_csp11_v2.dll";
    public string TokenLabelContains { get; set; } = "";
    public string TokenSerial { get; set; } = "";
    public string CertificateThumbprint { get; set; } = "";
    public string CertificateSubjectContains { get; set; } = "MISA-CA";
    public string EncryptedPin { get; set; } = "";
    public bool RequireKskRoot { get; set; } = true;
    public string FacilityCode { get; set; } = "";
    public long MaxRequestBytes { get; set; } = 2 * 1024 * 1024;
}

public sealed record SignXmlRequest(
    string Xml,
    string? Profile,
    string? RecordId,
    string? UserName,
    string? MachineName);


public sealed class ConfiguredPinLoginService
{
    private readonly Pkcs11TokenService _token;
    private readonly string _pin;
    private readonly object _sync = new();
    private bool _disabledAfterFailure;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_pin);

    public ConfiguredPinLoginService(Pkcs11TokenService token, SigningServerOptions options)
    {
        _token = token;
        _pin = PinProtection.TryUnprotect(options.EncryptedPin, out var pin) ? pin : string.Empty;
    }

    public void EnsureLoggedIn()
    {
        if (_token.IsLoggedIn) return;
        lock (_sync)
        {
            if (_token.IsLoggedIn) return;
            if (!IsConfigured)
                throw new Pkcs11TokenException("TOKEN_PIN_NOT_CONFIGURED", "Server chưa cấu hình mã PIN USB Token.");
            if (_disabledAfterFailure)
                throw new Pkcs11TokenException("TOKEN_PIN_LOGIN_FAILED", "Server đã đăng nhập Token thất bại. Hãy kiểm tra PIN trên Server và khởi động lại.");
            try
            {
                _token.Login(_pin);
            }
            catch
            {
                _disabledAfterFailure = true;
                throw;
            }
        }
    }
}

internal static class PinProtection
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("KSK-SIGN-SERVER-PIN-v11");

    public static bool TryUnprotect(string? encryptedPin, out string pin)
    {
        pin = string.Empty;
        if (string.IsNullOrWhiteSpace(encryptedPin)) return false;
        try
        {
            var encrypted = Convert.FromBase64String(encryptedPin);
            var plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            pin = Encoding.UTF8.GetString(plain);
            CryptographicOperations.ZeroMemory(plain);
            return true;
        }
        catch { return false; }
    }
}

public sealed class PinAttemptGuard
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan BlockDuration = TimeSpan.FromMinutes(10);
    private readonly object _sync = new();
    private readonly Dictionary<string, AttemptState> _states = new();

    public bool CanAttempt(string key, out DateTimeOffset blockedUntil)
    {
        lock (_sync)
        {
            if (_states.TryGetValue(key, out var state) &&
                state.BlockedUntil is { } until &&
                until > DateTimeOffset.Now)
            {
                blockedUntil = until;
                return false;
            }

            if (state?.BlockedUntil is not null)
                _states.Remove(key);

            blockedUntil = DateTimeOffset.MinValue;
            return true;
        }
    }

    public PinFailureResult RecordFailure(string key)
    {
        lock (_sync)
        {
            if (!_states.TryGetValue(key, out var state))
                _states[key] = state = new AttemptState();

            state.Failures++;
            if (state.Failures >= MaxAttempts)
            {
                state.BlockedUntil = DateTimeOffset.Now.Add(BlockDuration);
                return new PinFailureResult(true, 0);
            }

            return new PinFailureResult(false, MaxAttempts - state.Failures);
        }
    }

    public void RecordSuccess(string key)
    {
        lock (_sync)
            _states.Remove(key);
    }

    public void Block(string key)
    {
        lock (_sync)
            _states[key] = new AttemptState
            {
                Failures = MaxAttempts,
                BlockedUntil = DateTimeOffset.Now.Add(BlockDuration)
            };
    }

    private sealed class AttemptState
    {
        public int Failures { get; set; }
        public DateTimeOffset? BlockedUntil { get; set; }
    }
}

public readonly record struct PinFailureResult(
    bool Blocked,
    int RemainingAttempts);

public sealed class SigningQueue
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _waiting;

    public int WaitingCount => Volatile.Read(ref _waiting);

    public async Task EnterAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _waiting);
        try
        {
            await _gate.WaitAsync(cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref _waiting);
        }
    }

    public void Exit() => _gate.Release();
}

public sealed class ServerMetrics
{
    private long _success;
    private long _failure;
    private long _totalTicks;

    public void RecordSuccess(TimeSpan elapsed)
    {
        Interlocked.Increment(ref _success);
        Interlocked.Add(ref _totalTicks, elapsed.Ticks);
    }

    public void RecordFailure() => Interlocked.Increment(ref _failure);

    public object Snapshot()
    {
        var success = Interlocked.Read(ref _success);
        var failure = Interlocked.Read(ref _failure);
        var ticks = Interlocked.Read(ref _totalTicks);

        return new
        {
            success,
            failure,
            averageMilliseconds =
                success == 0 ? 0 : TimeSpan.FromTicks(ticks / success).TotalMilliseconds
        };
    }
}

public sealed class AuditLogService
{
    private readonly string _directory =
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "KSKSigningServer",
            "Logs");

    public AuditLogService() => Directory.CreateDirectory(_directory);

    public async Task WriteAsync(
        HttpContext ctx,
        SignXmlRequest request,
        bool success,
        string? error,
        string? thumbprint,
        long elapsedMilliseconds)
    {
        var entry = JsonSerializer.Serialize(new
        {
            time = DateTimeOffset.Now,
            remoteIp = ctx.Connection.RemoteIpAddress?.ToString(),
            request.RecordId,
            request.Profile,
            request.UserName,
            request.MachineName,
            success,
            error,
            thumbprint,
            elapsedMilliseconds
        });

        var path = Path.Combine(
            _directory,
            $"audit-{DateTime.Today:yyyy-MM-dd}.jsonl");

        await File.AppendAllTextAsync(path, entry + Environment.NewLine);
    }
}

public static class IpAccess
{
    public static bool IsAllowed(IPAddress? address, string[] rules)
    {
        if (address is null)
            return false;

        if (IPAddress.IsLoopback(address))
            return true;

        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        return rules.Any(rule => Match(address, rule));
    }

    private static bool Match(IPAddress address, string rule)
    {
        rule = (rule ?? "").Trim();
        if (rule.Length == 0)
            return false;

        if (!rule.Contains('/'))
            return IPAddress.TryParse(rule, out var exact) &&
                   address.Equals(exact.IsIPv4MappedToIPv6
                       ? exact.MapToIPv4()
                       : exact);

        var parts = rule.Split('/');
        if (parts.Length != 2 ||
            !IPAddress.TryParse(parts[0], out var network) ||
            !int.TryParse(parts[1], out var prefix))
            return false;

        var addressBytes = address.GetAddressBytes();
        var networkBytes = network.GetAddressBytes();
        if (addressBytes.Length != networkBytes.Length)
            return false;

        var fullBytes = prefix / 8;
        var remainingBits = prefix % 8;

        for (var i = 0; i < fullBytes; i++)
            if (addressBytes[i] != networkBytes[i])
                return false;

        if (remainingBits == 0)
            return true;

        var mask = (byte)(0xFF << (8 - remainingBits));
        return (addressBytes[fullBytes] & mask) ==
               (networkBytes[fullBytes] & mask);
    }
}
