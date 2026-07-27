using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace GKSKLaiXe.Services.LanSigning;

public sealed class LanSigningClient : IDisposable
{
    private readonly HttpClient _http;

    public LanSigningClient(string baseUrl, string apiKey, TimeSpan? timeout = null)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(NormalizeServerUrl(baseUrl), UriKind.Absolute),
            Timeout = timeout ?? TimeSpan.FromSeconds(60)
        };
        _http.DefaultRequestHeaders.TryAddWithoutValidation("X-API-Key", NormalizeApiKey(apiKey));
    }

    public static string NormalizeApiKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new LanSigningServerException("Thiếu API Key của Signing Server.",
                LanSigningErrorKind.InvalidConfiguration);

        var text = value.Trim();
        var markerIndex = text.LastIndexOf("API Key:", StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
            text = text[(markerIndex + "API Key:".Length)..].Trim();

        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch is '\r' or '\n' or '\t' or '\u200B' or '\u200C' or '\u200D' or '\uFEFF')
                continue;
            if (!char.IsWhiteSpace(ch))
                builder.Append(ch);
        }

        text = builder.ToString();
        if (text.Length == 0 || text.Any(ch => ch < 0x21 || ch > 0x7E))
            throw new LanSigningServerException("API Key không hợp lệ.",
                LanSigningErrorKind.InvalidConfiguration);

        return text;
    }

    public static string NormalizeServerUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new LanSigningServerException("Thiếu địa chỉ Signing Server.",
                LanSigningErrorKind.InvalidConfiguration);

        var text = value.Trim();
        var marker = text.IndexOf("Địa chỉ LAN:", StringComparison.OrdinalIgnoreCase);
        if (marker >= 0)
            text = text[(marker + "Địa chỉ LAN:".Length)..].Trim();

        var separator = text.IndexOf('|');
        if (separator >= 0)
            text = text[..separator].Trim();

        text = text.TrimEnd('/');
        if (!Uri.TryCreate(text + "/", UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new LanSigningServerException(
                "Địa chỉ Signing Server không hợp lệ. Ví dụ: http://192.168.1.10:7443",
                LanSigningErrorKind.InvalidConfiguration);

        return uri.ToString();
    }

    public async Task<LanSigningStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync("api/status", cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw CreateServerException(response.StatusCode, json);

            return JsonSerializer.Deserialize<LanSigningStatus>(json, JsonOptions())
                   ?? throw new LanSigningServerException(
                       "Signing Server trả dữ liệu trạng thái không hợp lệ.",
                       LanSigningErrorKind.InvalidResponse);
        }
        catch (Exception ex) when (ex is not LanSigningServerException)
        {
            throw ConvertConnectionException(ex, cancellationToken);
        }
    }

    public async Task<LanSignResult> SignXmlAsync(
        string unsignedXml, string profile, string? recordId,
        string? userName, CancellationToken cancellationToken = default)
    {
        return await SignXmlCoreAsync(
            unsignedXml, profile, recordId, userName, cancellationToken);
    }

    private async Task<LanSignResult> SignXmlCoreAsync(
        string unsignedXml, string profile, string? recordId,
        string? userName, CancellationToken cancellationToken)
    {
        var request = new
        {
            xml = unsignedXml,
            profile,
            recordId,
            userName,
            machineName = Environment.MachineName
        };

        try
        {
            using var response = await _http.PostAsJsonAsync(
                "api/sign-xml", request, JsonOptions(), cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<LanSignResult>(json, JsonOptions());

            if (result is null)
                throw new LanSigningServerException(
                    "Signing Server trả dữ liệu ký không hợp lệ.",
                    LanSigningErrorKind.InvalidResponse);

            if (!response.IsSuccessStatusCode || !result.Success)
                throw CreateServerException(response.StatusCode, json, result.Message, result.Code);

            return result;
        }
        catch (Exception ex) when (ex is not LanSigningServerException)
        {
            throw ConvertConnectionException(ex, cancellationToken);
        }
    }

    private static LanSigningServerException CreateServerException(
        HttpStatusCode statusCode,
        string json,
        string? fallbackMessage = null,
        string? fallbackCode = null)
    {
        var payload = ReadErrorPayload(json);
        var message = payload.Message ?? fallbackMessage
            ?? $"Signing Server trả lỗi HTTP {(int)statusCode}.";
        var code = payload.Code ?? fallbackCode;
        var normalized = (code ?? "").Trim().ToUpperInvariant();

        var kind = normalized switch
        {
            "TOKEN_SESSION_EXPIRED" or "TOKEN_NOT_LOGGED_IN" or "TOKEN_NOT_ACTIVATED"
                => LanSigningErrorKind.TokenLoginRequired,
            "TOKEN_PIN_INVALID" or "CKR_PIN_INCORRECT"
                => LanSigningErrorKind.PinInvalid,
            "TOKEN_PIN_LOCKED" or "CKR_PIN_LOCKED"
                => LanSigningErrorKind.PinLocked,
            "PIN_RETRY_BLOCKED"
                => LanSigningErrorKind.PinTemporarilyBlocked,
            _ when statusCode == HttpStatusCode.Conflict &&
                   (message.Contains("chưa được kích hoạt", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("hết hạn", StringComparison.OrdinalIgnoreCase))
                => LanSigningErrorKind.TokenLoginRequired,
            _ when message.Contains("PIN", StringComparison.OrdinalIgnoreCase) &&
                   (message.Contains("không", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("sai", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("từ chối", StringComparison.OrdinalIgnoreCase))
                => LanSigningErrorKind.PinInvalid,
            _ => LanSigningErrorKind.ServerRejected
        };

        return new LanSigningServerException(message, kind, code: code);
    }

    private static LanSigningServerException ConvertConnectionException(
        Exception ex, CancellationToken cancellationToken)
    {
        if (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            return new LanSigningServerException(
                "Hết thời gian chờ kết nối Signing Server.",
                LanSigningErrorKind.Timeout, ex);

        if (ex is HttpRequestException httpEx)
        {
            var kind = httpEx.InnerException is SocketException
                ? LanSigningErrorKind.ConnectionFailed
                : LanSigningErrorKind.NetworkError;

            return new LanSigningServerException(
                "Không thể kết nối tới Signing Server. Hãy kiểm tra Server, IP, cổng và mạng LAN.",
                kind, ex);
        }

        if (ex is UriFormatException)
            return new LanSigningServerException(
                "Địa chỉ Signing Server không hợp lệ.",
                LanSigningErrorKind.InvalidConfiguration, ex);

        return new LanSigningServerException(
            ex.Message, LanSigningErrorKind.Unknown, ex);
    }

    private static (string? Message, string? Code) ReadErrorPayload(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var message = root.TryGetProperty("message", out var m) ? m.GetString() : null;
            var code = root.TryGetProperty("code", out var c) ? c.GetString() : null;
            return (message, code);
        }
        catch
        {
            return (null, null);
        }
    }

    private static JsonSerializerOptions JsonOptions() =>
        new() { PropertyNameCaseInsensitive = true };

    public void Dispose() => _http.Dispose();
}

public sealed class LanSigningStatus
{
    public bool Success { get; set; }
    public string? Version { get; set; }
    public int QueueLength { get; set; }
    public bool PinConfigured { get; set; }
    public bool TokenActivated { get; set; }
    public string? Code { get; set; }
    public LanCertificateInfo? Certificate { get; set; }
}

public sealed class LanCertificateInfo
{
    public string? Subject { get; set; }
    public string? Thumbprint { get; set; }
    public DateTime NotAfter { get; set; }
}

public sealed class LanTokenActivationResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? Code { get; set; }
    public bool TokenActivated { get; set; }
    public LanCertificateInfo? Certificate { get; set; }
}

public sealed class LanSignResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? Code { get; set; }
    public bool TokenActivated { get; set; }
    public string? SignedXml { get; set; }
    public string? SignedBase64 { get; set; }
    public string? CertificateThumbprint { get; set; }
    public long ElapsedMilliseconds { get; set; }
}

public enum LanSigningErrorKind
{
    Unknown,
    InvalidConfiguration,
    ConnectionFailed,
    NetworkError,
    Timeout,
    ServerRejected,
    InvalidResponse,
    TokenLoginRequired,
    PinInvalid,
    PinLocked,
    PinTemporarilyBlocked,
    UserCancelled
}

public sealed class LanSigningServerException : Exception
{
    public LanSigningErrorKind Kind { get; }
    public string? Code { get; }

    public bool IsConnectionFailure => Kind is
        LanSigningErrorKind.ConnectionFailed or
        LanSigningErrorKind.NetworkError or
        LanSigningErrorKind.Timeout;

    public bool RequiresTokenLogin =>
        Kind == LanSigningErrorKind.TokenLoginRequired;

    public bool IsPinFailure =>
        Kind is LanSigningErrorKind.PinInvalid or
        LanSigningErrorKind.ServerRejected;

    public bool IsPinLocked =>
        Kind == LanSigningErrorKind.PinLocked;

    public LanSigningServerException(
        string message,
        LanSigningErrorKind kind,
        Exception? innerException = null,
        string? code = null)
        : base(message, innerException)
    {
        Kind = kind;
        Code = code;
    }
}
