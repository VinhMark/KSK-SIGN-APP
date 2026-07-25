using System.Collections.ObjectModel;
using System.Globalization;
using ClosedXML.Excel;
using GKSKLaiXe.Models;

namespace GKSKLaiXe.Services;

public sealed class ExcelService
{
    private static readonly string[] Columns =
    [
        "ServiceName","CreateDate","Paid","SO","HOTEN","GIOITINHVAL","NGAYSINH",
        "DIACHITHUONGTRU","MATINH_THUONGTRU","MAXA_THUONGTRU","SOCMND_PASSPORT",
        "NGAYTHANGNAMCAPCMND","NOICAP","IDBENHVIEN","BENHVIEN","NONGDOCON",
        "DVINONGDOCON","MATUY","NGAYKETLUAN","BACSYKETLUAN","KETLUAN",
        "HANGBANGLAI","LYDO","TINHTRANGBENH","STATE","SIGNDATA",
        "ValidationStatus","ErrorMessage","SendStatus","ApiMessage","UUID"
    ];

    public ObservableCollection<GkskRecord> Import(
        string path,
        DriverKskDefaults? defaults = null)
    {
        defaults ??=
            new DriverKskDefaults();

        using var wb =
            new XLWorkbook(path);

        var ws =
            wb.Worksheets.First();

        var header =
            ws.FirstRowUsed();

        if (header is null)
            return [];

        var map =
            header
                .CellsUsed()
                .ToDictionary(
                    c => NormalizeHeader(c.GetString()),
                    c => c.Address.ColumnNumber,
                    StringComparer.OrdinalIgnoreCase);

        string Get(
            IXLRow row,
            params string[] aliases)
        {
            foreach (var alias in aliases)
            {
                var key =
                    NormalizeHeader(alias);

                if (map.TryGetValue(
                        key,
                        out var col))
                {
                    return row
                        .Cell(col)
                        .GetFormattedString()
                        .Trim();
                }
            }

            return "";
        }

        var result =
            new ObservableCollection<GkskRecord>();

        foreach (var row in ws.RowsUsed().Skip(1))
        {
            if (row.IsEmpty())
                continue;

            var gender =
                NormalizeGender(
                    Get(
                        row,
                        "Giới tính",
                        "GIOITINHVAL"));

            result.Add(
                new GkskRecord
                {
                    SO =
                        Get(
                            row,
                            "Số KSK",
                            "SO"),

                    HANGBANGLAI =
                        Get(
                            row,
                            "Hạng GPLX",
                            "HANGBANGLAI"),

                    HOTEN =
                        Get(
                            row,
                            "Họ tên",
                            "Hộ tên",
                            "HOTEN"),

                    GIOITINHVAL =
                        gender,

                    NGAYSINH =
                        NormalizeDate(
                            Get(
                                row,
                                "Ngày sinh",
                                "NGAYSINH")),

                    SOCMND_PASSPORT =
                        Get(
                            row,
                            "CCCD",
                            "CCCD/CMND/Hộ chiếu",
                            "SOCMND_PASSPORT"),

                    NGAYTHANGNAMCAPCMND =
                        NormalizeDate(
                            Get(
                                row,
                                "Ngày cấp",
                                "NGAYTHANGNAMCAPCMND")),

                    NOICAP =
                        Get(
                            row,
                            "Nơi cấp",
                            "NOICAP"),

                    DIACHITHUONGTRU =
                        Get(
                            row,
                            "Địa chỉ",
                            "DIACHITHUONGTRU"),

                    MATINH_THUONGTRU =
                        Get(
                            row,
                            "Mã tỉnh",
                            "MATINH_THUONGTRU"),

                    MAXA_THUONGTRU =
                        Get(
                            row,
                            "Mã xã",
                            "MAXA_THUONGTRU"),

                    NGAYKETLUAN =
                        NormalizeDate(
                            Get(
                                row,
                                "Ngày kết luận",
                                "NGAYKETLUAN")),

                    // Các trường mặc định từ tab cấu hình.
                    IDBENHVIEN =
                        defaults.IDBENHVIEN,

                    BENHVIEN =
                        defaults.BENHVIEN,

                    MATUY =
                        string.IsNullOrWhiteSpace(defaults.MATUY)
                            ? "0"
                            : defaults.MATUY,

                    BACSYKETLUAN =
                        defaults.BACSYKETLUAN,

                    KETLUAN =
                        string.IsNullOrWhiteSpace(defaults.KETLUAN)
                            ? "A0-1"
                            : defaults.KETLUAN,

                    STATE =
                        string.IsNullOrWhiteSpace(defaults.STATE)
                            ? "ADD"
                            : defaults.STATE
                });
        }

        return result;
    }

    public void Export(string path, IEnumerable<GkskRecord> records)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("DATA_GKSK");

        for (var i = 0; i < Columns.Length; i++)
            ws.Cell(1, i + 1).Value = Columns[i];

        var row = 2;
        foreach (var r in records)
        {
            object?[] values =
            [
                r.ServiceName, r.CreateDate, r.Paid, r.SO, r.HOTEN, r.GIOITINHVAL, r.NGAYSINH,
                r.DIACHITHUONGTRU, r.MATINH_THUONGTRU, r.MAXA_THUONGTRU, r.SOCMND_PASSPORT,
                r.NGAYTHANGNAMCAPCMND, r.NOICAP, r.IDBENHVIEN, r.BENHVIEN, r.NONGDOCON,
                r.DVINONGDOCON, r.MATUY, r.NGAYKETLUAN, r.BACSYKETLUAN, r.KETLUAN,
                r.HANGBANGLAI, r.LYDO, r.TINHTRANGBENH, r.STATE, r.SIGNDATA,
                r.ValidationStatus, r.ErrorMessage, r.SendStatus, r.ApiMessage, r.UUID
            ];

            for (var col = 0; col < values.Length; col++)
                ws.Cell(row, col + 1).Value = XLCellValue.FromObject(values[col]);

            row++;
        }

        var range = ws.Range(1, 1, Math.Max(2, row - 1), Columns.Length);
        range.CreateTable();
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents(8, 45);
        wb.SaveAs(path);
    }

    public void ExportDriverKsk(
        string path,
        IEnumerable<GkskRecord> records)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("DATA_KSK_LAI_XE");

        string[] headers =
        [
            "Chọn",
            "Số KSK",
            "Người Giới Thiệu",
            "Gói Khám",
            "Ngày tạo",
            "Loại KSK",
            "Hạng GPLX",
            "Họ tên",
            "Ngày sinh",
            "Giới tính",
            "Thanh Toán",
            "CCCD/CMND/Hộ chiếu",
            "Ngày cấp",
            "Nơi cấp",
            "Địa chỉ"
        ];

        for (var i = 0; i < headers.Length; i++)
            ws.Cell(1, i + 1).Value = headers[i];

        int row = 2;

        foreach (var r in records)
        {
            object?[] values =
            [
                r.IsSelected ? "X" : "",
                r.SO,
                r.IntroName,
                r.ServiceName,
                r.CreateDate?.ToString("dd/MM/yyyy HH:mm") ?? "",
                r.DriverHealthCategory,
                r.HANGBANGLAI,
                r.HOTEN,
                r.NGAYSINH,
                r.GenderDisplay,
                r.PaidDisplay,
                r.SOCMND_PASSPORT,
                r.NGAYTHANGNAMCAPCMND,
                r.NOICAP,
                r.DIACHITHUONGTRU
            ];

            for (var col = 0; col < values.Length; col++)
                ws.Cell(row, col + 1).Value =
                    XLCellValue.FromObject(values[col]);

            row++;
        }

        var used = ws.RangeUsed();

        if (used is not null)
        {
            used.Style.Alignment.Vertical =
                XLAlignmentVerticalValues.Center;

            used.CreateTable();
        }

        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents(8, 45);

        foreach (var column in ws.ColumnsUsed())
        {
            if (column.Width > 42)
                column.Width = 42;
        }

        wb.SaveAs(path);
    }

    public void ExportGeneralKsk(
        string path,
        IEnumerable<GkskRecord> records)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("DATA_KSK");

        string[] headers =
        [
            "Chọn",
            "Số KSK",
            "Người giới thiệu",
            "Tên Gói Khám",
            "Ngày tạo",
            "Loại KSK",
            "Loại Sức khỏe",
            "Họ tên",
            "Ngày sinh",
            "Giới tính",
            "Thanh Toán"
        ];

        for (var i = 0; i < headers.Length; i++)
            ws.Cell(1, i + 1).Value = headers[i];

        int row = 2;

        foreach (var r in records)
        {
            object?[] values =
            [
                r.IsSelected ? "X" : "",
                r.SO,
                r.IntroName,
                r.ServiceName,
                r.CreateDate?.ToString("dd/MM/yyyy HH:mm") ?? "",
                r.GeneralHealthCategory,
                r.GeneralHealthType,
                r.HOTEN,
                r.NGAYSINH,
                r.GenderDisplay,
                r.PaidDisplay
            ];

            for (var col = 0; col < values.Length; col++)
                ws.Cell(row, col + 1).Value =
                    XLCellValue.FromObject(values[col]);

            row++;
        }

        var used = ws.RangeUsed();

        if (used is not null)
        {
            used.Style.Alignment.Vertical =
                XLAlignmentVerticalValues.Center;

            used.CreateTable();
        }

        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents(8, 45);

        foreach (var column in ws.ColumnsUsed())
        {
            if (column.Width > 42)
                column.Width = 42;
        }

        wb.SaveAs(path);
    }

    private static string NormalizeHeader(
        string value)
    {
        return (value ?? "")
            .Trim()
            .Replace(" ", "")
            .Replace("_", "")
            .ToUpperInvariant();
    }

    private static string NormalizeGender(
        string value)
    {
        var normalized =
            (value ?? "")
            .Trim()
            .ToUpperInvariant();

        return normalized switch
        {
            "0" or "NAM" => "0",
            "1" or "NỮ" or "NU" => "1",
            _ => value.Trim()
        };
    }

    private static string NormalizeDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        if (DateTime.TryParse(value, CultureInfo.GetCultureInfo("vi-VN"), DateTimeStyles.None, out var dt))
            return dt.ToString("dd/MM/yyyy");
        return value.Trim();
    }
}
