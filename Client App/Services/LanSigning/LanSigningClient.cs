using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace GKSKLaiXe.Services.LanSigning;

public sealed class LanSigningClient : IDisposable
{
    private readonly HttpClient _http;

    public LanSigningClient(string baseUrl, string apiKey, TimeSpan? timeout = null)
    {
        var normalizedUrl = NormalizeServerUrl(baseUrl);
        var normalizedApiKey = NormalizeApiKey(apiKey);

        _http = new HttpClient
        {
            BaseAddress = new Uri(normalizedUrl, UriKind.Absolute),
            Timeout = timeout ?? TimeSpan.FromSeconds(60)
        };

        // HTTP header values are restricted to ASCII. Validate first so the user gets
        // a clear configuration error instead of the low-level .NET exception:
        // "Request headers must contain only ASCII characters".
        _http.DefaultRequestHeaders.TryAddWithoutValidation("X-API-Key", normalizedApiKey);
    }


    public static string NormalizeApiKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new LanSigningServerException(
                "Thiếu API Key của Signing Server.",
                LanSigningErrorKind.InvalidConfiguration);

        var text = value.Trim();

        // Cho phép đại ca dán nguyên dòng được hiển thị ở Signing Manager:
        // "Địa chỉ LAN: ... | API Key: ABCDEF..."
        var markerIndex = text.LastIndexOf("API Key:", StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
            text = text[(markerIndex + "API Key:".Length)..].Trim();

        // Loại bỏ ký tự xuống dòng, zero-width và khoảng trắng Unicode thường xuất hiện
        // khi copy từ giao diện/chat. Không tự thay đổi các ký tự ASCII hợp lệ của khóa.
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch is '\r' or '\n' or '\t' or '\u200B' or '\u200C' or '\u200D' or '\uFEFF')
                continue;

            if (char.IsWhiteSpace(ch))
                continue;

            builder.Append(ch);
        }

        text = builder.ToString();
        if (text.Length == 0)
            throw new LanSigningServerException(
                "API Key đang trống.",
                LanSigningErrorKind.InvalidConfiguration);

        // Header HTTP chỉ chấp nhận ASCII in được (0x21-0x7E).
        if (text.Any(ch => ch < 0x21 || ch > 0x7E))
            throw new LanSigningServerException(
                "API Key chứa ký tự tiếng Việt hoặc ký tự đặc biệt không hợp lệ. Hãy sao chép đúng phần khóa sau chữ 'API Key:' trên Signing Server.",
                LanSigningErrorKind.InvalidConfiguration);

        return text;
    }

    public static string NormalizeServerUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new LanSigningServerException(
                "Thiếu địa chỉ Signing Server.",
                LanSigningErrorKind.InvalidConfiguration);

        var text = value.Trim();
        var addressMarker = text.IndexOf("Địa chỉ LAN:", StringComparison.OrdinalIgnoreCase);
        if (addressMarker >= 0)
            text = text[(addressMarker + "Địa chỉ LAN:".Length)..].Trim();

        var separator = text.IndexOf('|');
        if (separator >= 0)
            text = text[..separator].Trim();

        text = text.TrimEnd('/');
        if (!Uri.TryCreate(text + "/", UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new LanSigningServerException(
                "Địa chỉ Signing Server không hợp lệ. Ví dụ: http://192.168.1.10:7443",
                LanSigningErrorKind.InvalidConfiguration);
        }

        return uri.ToString();
    }

    public async Task<LanSigningStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync("api/status", cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new LanSigningServerException(
                    ReadMessage(json) ?? $"Signing Server trả lỗi HTTP {(int)response.StatusCode}.",
                    LanSigningErrorKind.ServerRejected);

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

    public async Task<LanSignResult> SignXmlAsync(string unsignedXml, string profile, string? recordId,
        string? userName, CancellationToken cancellationToken = default)
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
            using var response = await _http.PostAsJsonAsync("api/sign-xml", request, JsonOptions(), cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<LanSignResult>(json, JsonOptions());
            if (result is null)
                throw new LanSigningServerException(
                    "Signing Server trả dữ liệu ký không hợp lệ.",
                    LanSigningErrorKind.InvalidResponse);

            if (!response.IsSuccessStatusCode || !result.Success)
                throw new LanSigningServerException(
                    result.Message ?? $"Signing Server trả lỗi HTTP {(int)response.StatusCode}.",
                    LanSigningErrorKind.ServerRejected);

            return result;
        }
        catch (Exception ex) when (ex is not LanSigningServerException)
        {
            throw ConvertConnectionException(ex, cancellationToken);
        }
    }

    private static LanSigningServerException ConvertConnectionException(
        Exception ex,
        CancellationToken cancellationToken)
    {
        if (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new LanSigningServerException(
                "Hết thời gian chờ kết nối Signing Server.",
                LanSigningErrorKind.Timeout,
                ex);
        }

        if (ex is HttpRequestException httpEx)
        {
            var kind = httpEx.InnerException is SocketException
                ? LanSigningErrorKind.ConnectionFailed
                : LanSigningErrorKind.NetworkError;

            return new LanSigningServerException(
                "Không thể kết nối tới Signing Server. Hãy kiểm tra Server đang chạy, địa chỉ IP, cổng và mạng LAN.",
                kind,
                ex);
        }

        if (ex is UriFormatException)
        {
            return new LanSigningServerException(
                "Địa chỉ Signing Server không hợp lệ.",
                LanSigningErrorKind.InvalidConfiguration,
                ex);
        }

        return new LanSigningServerException(
            ex.Message,
            LanSigningErrorKind.Unknown,
            ex);
    }

    private static string? ReadMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("message", out var value) ? value.GetString() : null;
        }
        catch { return null; }
    }

    private static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true };
    public void Dispose() => _http.Dispose();
}

public sealed class LanSigningStatus
{
    public bool Success { get; set; }
    public string? Version { get; set; }
    public int QueueLength { get; set; }
    public LanCertificateInfo? Certificate { get; set; }
}

public sealed class LanCertificateInfo
{
    public string? Subject { get; set; }
    public string? Thumbprint { get; set; }
    public DateTime NotAfter { get; set; }
}

public sealed class LanSignResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
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
    InvalidResponse
}

public sealed class LanSigningServerException : Exception
{
    public LanSigningErrorKind Kind { get; }

    public bool IsConnectionFailure => Kind is
        LanSigningErrorKind.ConnectionFailed or
        LanSigningErrorKind.NetworkError or
        LanSigningErrorKind.Timeout;

    public LanSigningServerException(
        string message,
        LanSigningErrorKind kind,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }
}
