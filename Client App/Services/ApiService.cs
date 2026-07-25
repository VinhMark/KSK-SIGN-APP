using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GKSKLaiXe.Models;

namespace GKSKLaiXe.Services;

public sealed class ApiService
{
    private readonly HttpClient _http =
        new()
        {
            Timeout =
                TimeSpan.FromSeconds(60)
        };

    public async Task<ApiResult> SendAsync(
        GkskRecord r,
        ApiSettings settings,
        bool includeSignData)
    {
        if (string.IsNullOrWhiteSpace(settings.Username) ||
            string.IsNullOrWhiteSpace(settings.Password))
        {
            return new ApiResult(
                false,
                "Chưa cấu hình Username/Password liên thông.",
                "");
        }

        using var req =
            new HttpRequestMessage(
                HttpMethod.Post,
                settings.Url);

        req.Headers.TryAddWithoutValidation(
            "Username",
            settings.Username);

        req.Headers.TryAddWithoutValidation(
            "Password",
            Md5(settings.Password));

        // Dùng Dictionary để có thể bỏ hoàn toàn trường SIGNDATA
        // khỏi payload khi checkbox Ký số không được chọn.
        var payload =
            new Dictionary<string, object?>
            {
                ["SO"] = r.SO,
                ["HOTEN"] = r.HOTEN,
                ["GIOITINHVAL"] = r.GIOITINHVAL,
                ["NGAYSINH"] = r.NGAYSINH,
                ["DIACHITHUONGTRU"] = r.DIACHITHUONGTRU,
                ["MATINH_THUONGTRU"] = r.MATINH_THUONGTRU,
                ["MAXA_THUONGTRU"] = r.MAXA_THUONGTRU,
                ["SOCMND_PASSPORT"] = r.SOCMND_PASSPORT,
                ["NGAYTHANGNAMCAPCMND"] = r.NGAYTHANGNAMCAPCMND,
                ["NOICAP"] = r.NOICAP,
                ["IDBENHVIEN"] = r.IDBENHVIEN,
                ["BENHVIEN"] = r.BENHVIEN,
                ["NONGDOCON"] = r.NONGDOCON,
                ["DVINONGDOCON"] = r.DVINONGDOCON,
                ["MATUY"] = r.MATUY,
                ["NGAYKETLUAN"] = r.NGAYKETLUAN,
                ["BACSYKETLUAN"] = r.BACSYKETLUAN,
                ["KETLUAN"] = r.KETLUAN,
                ["HANGBANGLAI"] = r.HANGBANGLAI,
                ["LYDO"] = r.LYDO,
                ["TINHTRANGBENH"] = r.TINHTRANGBENH,
                ["STATE"] = r.STATE
            };

        payload["SIGNDATA"] =
            includeSignData
                ? r.SIGNDATA
                : "test";

        req.Content =
            new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

        try
        {
            using var res =
                await _http.SendAsync(req);

            var body =
                await res.Content.ReadAsStringAsync();

            string message =
                body;

            string uuid =
                "";

            bool success =
                res.IsSuccessStatusCode;

            try
            {
                using var doc =
                    JsonDocument.Parse(body);

                var root =
                    doc.RootElement;

                if (root.TryGetProperty(
                        "MSG_TEXT",
                        out var msg))
                {
                    message =
                        msg.GetString() ??
                        body;
                }

                if (root.TryGetProperty(
                        "UUID",
                        out var id))
                {
                    uuid =
                        id.GetString() ??
                        "";
                }

                if (root.TryGetProperty(
                        "MSG_STATE",
                        out var state))
                {
                    success =
                        state.GetString() == "1";
                }
            }
            catch
            {
                // Nếu response không phải JSON thì giữ nguyên nội dung body.
            }

            return new ApiResult(
                success,
                message,
                uuid);
        }
        catch (Exception ex)
        {
            return new ApiResult(
                false,
                ex.Message,
                "");
        }
    }

    private static string Md5(
        string input)
    {
        var hash =
            MD5.HashData(
                Encoding.UTF8.GetBytes(input));

        return Convert
            .ToHexString(hash)
            .ToLowerInvariant();
    }
}

public sealed record ApiResult(
    bool Success,
    string Message,
    string UUID);
