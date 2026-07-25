using System.Collections.ObjectModel;
using System.Globalization;
using GKSKLaiXe.Models;
using Microsoft.Data.SqlClient;

namespace GKSKLaiXe.Services;

public sealed class SqlDataService
{
    public string BuildConnectionString(SqlSettings s)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = s.DataSource,
            InitialCatalog = s.Database,
            UserID = s.Username,
            Password = s.Password,
            TrustServerCertificate = true,
            Encrypt = false,
            ConnectTimeout = 10,
            ApplicationName = "GKSK Lai Xe"
        };
        return builder.ConnectionString;
    }

    public async Task TestConnectionAsync(SqlSettings settings)
    {
        await using var cn = new SqlConnection(BuildConnectionString(settings));
        await cn.OpenAsync();
    }

    public async Task<ObservableCollection<GkskRecord>> LoadAsync(
        SqlSettings settings,
        DateTime fromDate,
        DateTime toDate,
        bool onlyPaid,
        IReadOnlyCollection<string>? selectedPackages = null)
    {
        var result = new ObservableCollection<GkskRecord>();

        var packageNames =
            selectedPackages?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
            ?? [];

        string packageFilter;

        if (packageNames.Count == 0)
        {
            // Không chọn gói nào thì không lấy dữ liệu.
            packageFilter = "1 = 0";
        }
        else
        {
            var parameters =
                packageNames
                    .Select((_, index) => $"@Package{index}")
                    .ToArray();

            packageFilter =
                $"sp.ServicePackageName IN ({string.Join(", ", parameters)})";
        }

        string sql = $"""

        SELECT
            ksk.ServicePackageName AS ServiceName,
            ksk.CreateDate,
            ksk.Paid,

            LTRIM(RTRIM(
                CASE
                    WHEN CHARINDEX('-', REVERSE(ISNULL(pr.Reason, ''))) > 0
                    THEN LEFT(
                        pr.Reason,
                        LEN(pr.Reason) - CHARINDEX('-', REVERSE(pr.Reason))
                    )
                    ELSE ISNULL(pr.Reason, '')
                END
            )) AS SO,

            LTRIM(RTRIM(
                CASE
                    WHEN CHARINDEX('-', REVERSE(ISNULL(pr.Reason, ''))) > 0
                    THEN RIGHT(
                        pr.Reason,
                        CHARINDEX('-', REVERSE(pr.Reason)) - 1
                    )
                    ELSE ''
                END
            )) AS HANGBANGLAI,

            p.PatientName AS HOTEN,
            ISNULL(pr.IntroName, '') AS IntroName,
            CASE
                WHEN CONVERT(varchar(10), p.PatientGender) = '1' THEN '0'
                WHEN CONVERT(varchar(10), p.PatientGender) = '0' THEN '1'
                ELSE CONVERT(varchar(10), p.PatientGender)
            END AS GIOITINHVAL,
            p.PatientBirthday AS NGAYSINH,

            pr.Address AS DIACHITHUONGTRU,
            pr.ProvincialCode AS MATINH_THUONGTRU,
            pr.WardCode AS MAXA_THUONGTRU,
            pr.CMND AS SOCMND_PASSPORT,
            pr.CMND_NgayCap AS NGAYTHANGNAMCAPCMND,
            pr.CMND_NoiCap AS NOICAP,

            ci.DT_MaBH_CSKCB AS IDBENHVIEN,
            ci.HD_TenDV AS BENHVIEN,

            CAST('' AS nvarchar(50)) AS NONGDOCON,
            CAST('' AS nvarchar(10)) AS DVINONGDOCON,
            CAST('0' AS varchar(1)) AS MATUY,

            CONVERT(varchar(10), ksk.CreateDate, 103) AS NGAYKETLUAN,
            ci.Doctor AS BACSYKETLUAN,
            CAST('A0-1' AS varchar(10)) AS KETLUAN,

            CAST('' AS nvarchar(255)) AS LYDO,
            CAST('' AS nvarchar(255)) AS TINHTRANGBENH,
            CAST('ADD' AS varchar(5)) AS STATE,
            CAST('' AS nvarchar(max)) AS SIGNDATA

        FROM dbo.PatientReceive pr

        INNER JOIN dbo.Patients p
            ON p.PatientCode = pr.PatientCode

        CROSS APPLY
        (
            SELECT TOP (1)
                ssr.ReceiptID,
                ssr.CreateDate,
                ssr.Paid,
                sp.ServicePackageName
            FROM dbo.SuggestedServiceReceipt ssr
            INNER JOIN dbo.ServicePackage sp
                ON sp.ServicePackageCode = ssr.ServicePackageCode
            WHERE ssr.RefID = pr.PatientReceiveID
              AND ({packageFilter})
              AND (@OnlyPaid = 0 OR ISNULL(ssr.Paid, 0) = 1)
            ORDER BY ssr.CreateDate DESC, ssr.ReceiptID DESC
        ) ksk

        CROSS JOIN
        (
            SELECT TOP (1)
                DT_MaBH_CSKCB,
                HD_TenDV,
                Doctor
            FROM dbo.ClinicInformation
            ORDER BY ClinicCode
        ) ci

        WHERE pr.CreateDate >= @DateFrom
          AND pr.CreateDate < DATEADD(day, 1, @DateTo)

        ORDER BY pr.CreateDate DESC, pr.PatientReceiveID DESC;

        """;

        await using var cn = new SqlConnection(BuildConnectionString(settings));
        await cn.OpenAsync();

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@DateFrom", fromDate.Date);
        cmd.Parameters.AddWithValue("@DateTo", toDate.Date);
        cmd.Parameters.AddWithValue("@OnlyPaid", onlyPaid ? 1 : 0);

        for (var i = 0; i < packageNames.Count; i++)
        {
            cmd.Parameters.AddWithValue(
                $"@Package{i}",
                packageNames[i]);
        }

        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            result.Add(new GkskRecord
            {
                ServiceName = GetString(rd, "ServiceName"),
                CreateDate = GetDateTime(rd, "CreateDate"),
                Paid = GetInt(rd, "Paid"),
                SO = GetString(rd, "SO"),
                HANGBANGLAI = GetString(rd, "HANGBANGLAI"),
                HOTEN = GetString(rd, "HOTEN"),
                IntroName = GetString(rd, "IntroName"),
                GIOITINHVAL = GetString(rd, "GIOITINHVAL"),
                NGAYSINH = NormalizeDate(GetString(rd, "NGAYSINH")),
                DIACHITHUONGTRU = GetString(rd, "DIACHITHUONGTRU"),
                MATINH_THUONGTRU = GetString(rd, "MATINH_THUONGTRU"),
                MAXA_THUONGTRU = GetString(rd, "MAXA_THUONGTRU"),
                SOCMND_PASSPORT = GetString(rd, "SOCMND_PASSPORT"),
                NGAYTHANGNAMCAPCMND = NormalizeDate(GetString(rd, "NGAYTHANGNAMCAPCMND")),
                NOICAP = GetString(rd, "NOICAP"),
                IDBENHVIEN = GetString(rd, "IDBENHVIEN"),
                BENHVIEN = GetString(rd, "BENHVIEN"),
                NONGDOCON = GetString(rd, "NONGDOCON"),
                DVINONGDOCON = GetString(rd, "DVINONGDOCON"),
                MATUY = GetString(rd, "MATUY"),
                NGAYKETLUAN = GetString(rd, "NGAYKETLUAN"),
                BACSYKETLUAN = GetString(rd, "BACSYKETLUAN"),
                KETLUAN = GetString(rd, "KETLUAN"),
                LYDO = GetString(rd, "LYDO"),
                TINHTRANGBENH = GetString(rd, "TINHTRANGBENH"),
                STATE = GetString(rd, "STATE"),
                SIGNDATA = GetString(rd, "SIGNDATA")
            });
        }

        return result;
    }


    public async Task<IReadOnlyList<string>> LoadPackageNamesAsync(
        SqlSettings settings)
    {
        const string sql =
            """
            SELECT DISTINCT
                ServicePackageName
            FROM dbo.ServicePackage
            WHERE ISNULL(Hide, 0) = 0
              AND ISNULL(ServicePackageName, '') <> ''
            ORDER BY ServicePackageName;
            """;

        var result = new List<string>();

        await using var cn =
            new SqlConnection(BuildConnectionString(settings));

        await cn.OpenAsync();

        await using var cmd =
            new SqlCommand(sql, cn);

        await using var rd =
            await cmd.ExecuteReaderAsync();

        while (await rd.ReadAsync())
        {
            var name =
                rd.IsDBNull(0)
                    ? ""
                    : rd.GetString(0).Trim();

            if (!string.IsNullOrWhiteSpace(name))
                result.Add(name);
        }

        return result;
    }

    private static string GetString(SqlDataReader rd, string name) =>
        rd[name] == DBNull.Value ? "" : Convert.ToString(rd[name])?.Trim() ?? "";

    private static DateTime? GetDateTime(SqlDataReader rd, string name) =>
        rd[name] == DBNull.Value ? null : Convert.ToDateTime(rd[name]);

    private static int? GetInt(SqlDataReader rd, string name) =>
        rd[name] == DBNull.Value ? null : Convert.ToInt32(rd[name]);

    private static string NormalizeDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        string[] formats = ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "MM/dd/yyyy"];
        if (DateTime.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dt))
            return dt.ToString("dd/MM/yyyy");
        if (DateTime.TryParse(value, out dt))
            return dt.ToString("dd/MM/yyyy");
        return value.Trim();
    }
}
