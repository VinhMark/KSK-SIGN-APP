using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace GKSKLaiXe.Models;

public sealed class GkskRecord : INotifyPropertyChanged
{
    private bool _isSelected;
    private string _validationStatus = "";
    private string _errorMessage = "";
    private string _sendStatus = "";
    private string _apiMessage = "";
    private string _uuid = "";
    private string _ngaySinh = "";
    private string _ngayCap = "";
    private string _ngayKetLuan = "";

    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    public string ServiceName { get; set; } = "";
    public string IntroName { get; set; } = "";
    public DateTime? CreateDate { get; set; }
    public int? Paid { get; set; }

    public string GeneralHealthType { get; set; } = "LOẠI 1";
    public string GeneralHealthCategory => "KHÁM SỨC KHỎE";
    public string DriverHealthCategory => "KHÁM SỨC KHỎE LÁI XE";

    public string GenderDisplay =>
        GIOITINHVAL == "0" ? "Nam" :
        GIOITINHVAL == "1" ? "Nữ" : GIOITINHVAL;

    public string PaidDisplay =>
        Paid == 1 ? "Đã thanh toán" : "Chưa thanh toán";

    public string SO { get; set; } = "";
    public string HOTEN { get; set; } = "";
    public string GIOITINHVAL { get; set; } = "";

    // Mọi dữ liệu ngày đưa lên màn hình, kể cả import Excel,
    // đều được chuẩn hóa về dd/MM/yyyy.
    public string NGAYSINH
    {
        get => _ngaySinh;
        set => _ngaySinh = NormalizeDate(value);
    }

    public string DIACHITHUONGTRU { get; set; } = "";
    public string MATINH_THUONGTRU { get; set; } = "";
    public string MAXA_THUONGTRU { get; set; } = "";
    public string SOCMND_PASSPORT { get; set; } = "";

    public string NGAYTHANGNAMCAPCMND
    {
        get => _ngayCap;
        set => _ngayCap = NormalizeDate(value);
    }

    public string NOICAP { get; set; } = "";
    public string IDBENHVIEN { get; set; } = "";
    public string BENHVIEN { get; set; } = "";
    public string NONGDOCON { get; set; } = "";
    public string DVINONGDOCON { get; set; } = "";
    public string MATUY { get; set; } = "0";

    public string NGAYKETLUAN
    {
        get => _ngayKetLuan;
        set => _ngayKetLuan = NormalizeDate(value);
    }

    public string BACSYKETLUAN { get; set; } = "";
    public string KETLUAN { get; set; } = "A0-1";
    public string HANGBANGLAI { get; set; } = "";
    public string LYDO { get; set; } = "";
    public string TINHTRANGBENH { get; set; } = "";
    public string STATE { get; set; } = "ADD";
    public string SIGNDATA { get; set; } = "";
    public bool IsSent { get; set; }

    public string ValidationStatus { get => _validationStatus; set => Set(ref _validationStatus, value); }
    public string ErrorMessage { get => _errorMessage; set => Set(ref _errorMessage, value); }
    public string SendStatus { get => _sendStatus; set => Set(ref _sendStatus, value); }
    public string ApiMessage { get => _apiMessage; set => Set(ref _apiMessage, value); }
    public string UUID { get => _uuid; set => Set(ref _uuid, value); }

    public void Normalize()
    {
        SO = (SO ?? "").Trim();
        HOTEN = (HOTEN ?? "").Trim();
        GIOITINHVAL = (GIOITINHVAL ?? "").Trim();
        NGAYSINH = NGAYSINH;
        DIACHITHUONGTRU = (DIACHITHUONGTRU ?? "").Trim();
        MATINH_THUONGTRU = (MATINH_THUONGTRU ?? "").Trim();
        MAXA_THUONGTRU = (MAXA_THUONGTRU ?? "").Trim();
        SOCMND_PASSPORT = (SOCMND_PASSPORT ?? "").Trim();
        NGAYTHANGNAMCAPCMND = NGAYTHANGNAMCAPCMND;
        NOICAP = (NOICAP ?? "").Trim();
        IDBENHVIEN = (IDBENHVIEN ?? "").Trim();
        BENHVIEN = (BENHVIEN ?? "").Trim();
        NONGDOCON = (NONGDOCON ?? "").Trim();
        DVINONGDOCON = (DVINONGDOCON ?? "").Trim();
        MATUY = (MATUY ?? "").Trim();
        NGAYKETLUAN = NGAYKETLUAN;
        BACSYKETLUAN = (BACSYKETLUAN ?? "").Trim();
        KETLUAN = (KETLUAN ?? "").Trim();
        HANGBANGLAI = (HANGBANGLAI ?? "").Trim();
        LYDO = (LYDO ?? "").Trim();
        TINHTRANGBENH = (TINHTRANGBENH ?? "").Trim();
        STATE = (STATE ?? "").Trim();
        SIGNDATA = (SIGNDATA ?? "").Trim();
    }

    private static string NormalizeDate(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0)
            return "";

        string[] exactFormats =
        [
            "dd/MM/yyyy", "d/M/yyyy",
            "dd-MM-yyyy", "d-M-yyyy",
            "yyyy-MM-dd", "yyyy/MM/dd",
            "MM/dd/yyyy", "M/d/yyyy",
            "dd/MM/yyyy HH:mm:ss", "d/M/yyyy H:mm:ss"
        ];

        if (DateTime.TryParseExact(
                text,
                exactFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var exact))
            return exact.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

        if (DateTime.TryParse(
                text,
                CultureInfo.GetCultureInfo("vi-VN"),
                DateTimeStyles.AllowWhiteSpaces,
                out var viDate))
            return viDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

        if (DateTime.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var invariantDate))
            return invariantDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

        return text;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
