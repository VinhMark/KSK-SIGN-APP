using System.Globalization;
using System.Text.RegularExpressions;
using GKSKLaiXe.Models;

namespace GKSKLaiXe.Services;

public sealed class ValidationService
{
    private static readonly string[] DateFormats = ["dd/MM/yyyy", "d/M/yyyy"];

    public IReadOnlyList<string> Validate(GkskRecord r)
    {
        r.Normalize();
        var errors = new List<string>();

        Required(r.HOTEN, "Họ tên", errors);
        Required(r.GIOITINHVAL, "Giới tính", errors);
        Required(r.NGAYSINH, "Ngày sinh", errors);
        Required(r.SOCMND_PASSPORT, "CCCD/CMND/Hộ chiếu", errors);
        Required(r.NGAYTHANGNAMCAPCMND, "Ngày cấp CCCD/CMND/Hộ chiếu", errors);
        Required(r.NOICAP, "Nơi cấp", errors);
        Required(r.MATINH_THUONGTRU, "Mã tỉnh", errors);
        Required(r.MAXA_THUONGTRU, "Mã xã", errors);
        Required(r.DIACHITHUONGTRU, "Địa chỉ", errors);
        Required(r.SO, "Số giấy khám sức khỏe", errors);
        Required(r.BENHVIEN, "Cơ sở KCB", errors);
        Required(r.IDBENHVIEN, "Mã CSKCB", errors);
        Required(r.MATUY, "Ma túy", errors);
        Required(r.NGAYKETLUAN, "Ngày kết luận", errors);
        Required(r.BACSYKETLUAN, "Bác sĩ kết luận", errors);
        Required(r.KETLUAN, "Kết luận", errors);
        Required(r.HANGBANGLAI, "Hạng bằng lái", errors);

        if (!string.IsNullOrWhiteSpace(r.SO) && r.SO.Length > 21)
            errors.Add($"SO vượt 21 ký tự ({r.SO.Length}/21)");

        ValidateDate(r.NGAYSINH, "Ngày sinh", errors);
        ValidateDate(r.NGAYTHANGNAMCAPCMND, "Ngày cấp CCCD/CMND/Hộ chiếu", errors);
        ValidateDate(r.NGAYKETLUAN, "Ngày kết luận", errors);

        if (!string.IsNullOrWhiteSpace(r.SOCMND_PASSPORT) &&
            !IsValidIdentityOrPassport(r.SOCMND_PASSPORT))
            errors.Add("CCCD/CMND/Hộ chiếu sai định dạng");

        var allowedKetLuan = new[] { "A0-1", "A0-2", "A0-3" };
        if (!string.IsNullOrWhiteSpace(r.KETLUAN) &&
            !allowedKetLuan.Contains(r.KETLUAN.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            errors.Add("KẾT LUẬN chỉ được phép là A0-1, A0-2 hoặc A0-3");
        }

        var allowedMatuy = new[] { "0", "1" };
        if (!string.IsNullOrWhiteSpace(r.MATUY) &&
            !allowedMatuy.Contains(r.MATUY.Trim()))
        {
            errors.Add("MA TÚY chỉ được phép là 0 (Âm tính) hoặc 1 (Dương tính)");
        }

        var allowedState = new[] { "ADD", "EDIT" };
        if (!string.IsNullOrWhiteSpace(r.STATE) &&
            !allowedState.Contains(r.STATE.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            errors.Add("STATE chỉ được phép là ADD hoặc EDIT");
        }

        var allowedHangBangLai = new[]
        {
            "A", "A.03", "B", "B1", "B0.1", "BE",
            "C", "C1", "CE", "C1E",
            "D", "D2", "D2E", "DE"
        };

        if (!string.IsNullOrWhiteSpace(r.HANGBANGLAI) &&
            !allowedHangBangLai.Contains(
                r.HANGBANGLAI.Trim(),
                StringComparer.OrdinalIgnoreCase))
        {
            errors.Add("Hạng bằng lái không thuộc danh mục cho phép");
        }

        return errors;
    }

    private static void Required(string value, string name, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            errors.Add($"Thiếu {name}");
    }

    private static void ValidateDate(string value, string name, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (!DateTime.TryParseExact(value.Trim(), DateFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _))
            errors.Add($"{name} sai định dạng dd/MM/yyyy");
    }

    // Chấp nhận: CMND 9 số, CCCD 12 số, hoặc hộ chiếu 7-12 ký tự chữ/số.
    private static bool IsValidIdentityOrPassport(string value)
    {
        var v = Regex.Replace(value.Trim(), @"\s+", "");
        return Regex.IsMatch(v, @"^\d{9}$") ||
               Regex.IsMatch(v, @"^\d{12}$") ||
               Regex.IsMatch(v, @"^[A-Za-z][A-Za-z0-9]{6,11}$");
    }
}
