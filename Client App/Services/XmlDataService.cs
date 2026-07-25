using System.Data;
using GKSKLaiXe.Models;
using Microsoft.Data.SqlClient;

namespace GKSKLaiXe.Services;

public sealed class XmlDataService
{
    private static string BuildConnectionString(SqlSettings s)
    {
        return new SqlConnectionStringBuilder
        {
            DataSource = s.DataSource,
            InitialCatalog = s.Database,
            UserID = s.Username,
            Password = s.Password,
            TrustServerCertificate = true,
            Encrypt = false,
            ConnectTimeout = 15
        }.ConnectionString;
    }

    public sealed record XmlDataResult(
        DataTable Xml1,
        DataTable Xml2,
        DataTable Xml3);

    public async Task<XmlDataResult> LoadAllXmlAsync(
        SqlSettings settings,
        DateTime fromDate,
        DateTime toDate,
        string? maLk)
    {
        var xml1 = new DataTable();
        var xml2 = new DataTable();
        var xml3 = new DataTable();

        await using var cn =
            new SqlConnection(BuildConnectionString(settings));

        await cn.OpenAsync();

        await using var cmd =
            new SqlCommand(AllXmlSql, cn)
            {
                CommandTimeout = 120
            };

        cmd.Parameters.AddWithValue("@FromDate", fromDate.Date);
        cmd.Parameters.AddWithValue("@ToDate", toDate.Date);
        cmd.Parameters.AddWithValue(
            "@MaLK",
            string.IsNullOrWhiteSpace(maLk)
                ? DBNull.Value
                : maLk.Trim());

        await using var rd =
            await cmd.ExecuteReaderAsync();

        // DataTable.Load(reader) có thể tiêu thụ/đóng reader sau ResultSet đầu,
        // nên dùng DataSet.Load để nạp liên tiếp cả 3 ResultSet trong một lần.
        var dataSet = new DataSet();

        dataSet.Load(
            rd,
            LoadOption.OverwriteChanges,
            "XML1",
            "XML2",
            "XML3");

        if (dataSet.Tables.Contains("XML1"))
            xml1 = dataSet.Tables["XML1"]!;

        if (dataSet.Tables.Contains("XML2"))
            xml2 = dataSet.Tables["XML2"]!;

        if (dataSet.Tables.Contains("XML3"))
            xml3 = dataSet.Tables["XML3"]!;

        return new XmlDataResult(xml1, xml2, xml3);
    }

    public Task<DataTable> LoadXml1Async(
        SqlSettings settings,
        DateTime fromDate,
        DateTime toDate,
        string? maLk)
        => ExecuteAsync(settings, Xml1Sql, fromDate, toDate, maLk);

    public Task<DataTable> LoadXml2Async(
        SqlSettings settings,
        DateTime fromDate,
        DateTime toDate,
        string? maLk)
        => ExecuteAsync(settings, Xml2Sql, fromDate, toDate, maLk);

    public Task<DataTable> LoadXml3Async(
        SqlSettings settings,
        DateTime fromDate,
        DateTime toDate,
        string? maLk)
        => ExecuteAsync(settings, Xml3Sql, fromDate, toDate, maLk);

    private static async Task<DataTable> ExecuteAsync(
        SqlSettings settings,
        string sql,
        DateTime fromDate,
        DateTime toDate,
        string? maLk)
    {
        var table = new DataTable();

        await using var cn =
            new SqlConnection(BuildConnectionString(settings));

        await cn.OpenAsync();

        await using var cmd =
            new SqlCommand(sql, cn)
            {
                CommandTimeout = 120
            };

        cmd.Parameters.AddWithValue("@FromDate", fromDate.Date);
        cmd.Parameters.AddWithValue("@ToDate", toDate.Date);
        cmd.Parameters.AddWithValue(
            "@MaLK",
            string.IsNullOrWhiteSpace(maLk)
                ? DBNull.Value
                : maLk.Trim());

        await using var rd =
            await cmd.ExecuteReaderAsync();

        table.Load(rd);

        return table;
    }

    private const string AllXmlSql = """

IF OBJECT_ID('tempdb..#DOCTOR_LOOKUP') IS NOT NULL DROP TABLE #DOCTOR_LOOKUP;
IF OBJECT_ID('tempdb..#DEPARTMENT_LOOKUP') IS NOT NULL DROP TABLE #DEPARTMENT_LOOKUP;

CREATE TABLE #DOCTOR_LOOKUP
(
    Code nvarchar(255) NOT NULL,
    Name nvarchar(500) NULL
);

CREATE TABLE #DEPARTMENT_LOOKUP
(
    Code nvarchar(255) NOT NULL,
    Name nvarchar(500) NULL
);

DECLARE @DoctorTable nvarchar(512) = NULL;
DECLARE @DoctorCodeCol sysname = NULL;
DECLARE @DoctorNameCol sysname = NULL;
DECLARE @DepartmentTable nvarchar(512) = NULL;
DECLARE @DepartmentCodeCol sysname = NULL;
DECLARE @DepartmentNameCol sysname = NULL;

;WITH T AS
(
    SELECT
        QUOTENAME(SCHEMA_NAME(t.schema_id)) + '.' + QUOTENAME(t.name) AS TableName,
        t.object_id
    FROM sys.tables t
)
SELECT TOP (1)
    @DoctorTable = T.TableName,
    @DoctorCodeCol = CCode.name,
    @DoctorNameCol = CName.name
FROM T
CROSS APPLY
(
    SELECT TOP (1) c.name
    FROM sys.columns c
    WHERE c.object_id = T.object_id
      AND UPPER(c.name) IN ('EMPLOYEECODE','DOCTORCODE','BACSI_CODE','MA_BAC_SI','MACCHN')
) CCode
CROSS APPLY
(
    SELECT TOP (1) c.name
    FROM sys.columns c
    WHERE c.object_id = T.object_id
      AND UPPER(c.name) IN ('EMPLOYEENAME','DOCTORNAME','FULLNAME','TEN_BAC_SI','NAME')
) CName
ORDER BY
    CASE WHEN T.TableName LIKE '%Employee%' THEN 0 ELSE 1 END,
    T.TableName;

IF @DoctorTable IS NOT NULL
BEGIN
    DECLARE @SqlDoctor nvarchar(max) =
        N'INSERT INTO #DOCTOR_LOOKUP(Code, Name)
          SELECT DISTINCT
              CONVERT(nvarchar(255), ' + QUOTENAME(@DoctorCodeCol) + N'),
              CONVERT(nvarchar(500), ' + QUOTENAME(@DoctorNameCol) + N')
          FROM ' + @DoctorTable + N'
          WHERE ' + QUOTENAME(@DoctorCodeCol) + N' IS NOT NULL;';

    EXEC sp_executesql @SqlDoctor;
END;

;WITH T AS
(
    SELECT
        QUOTENAME(SCHEMA_NAME(t.schema_id)) + '.' + QUOTENAME(t.name) AS TableName,
        t.object_id
    FROM sys.tables t
)
SELECT TOP (1)
    @DepartmentTable = T.TableName,
    @DepartmentCodeCol = CCode.name,
    @DepartmentNameCol = CName.name
FROM T
CROSS APPLY
(
    SELECT TOP (1) c.name
    FROM sys.columns c
    WHERE c.object_id = T.object_id
      AND UPPER(c.name) IN ('DEPARTMENTCODE','DEPTCODE','MA_KHOA','KHOACODE','CODE')
) CCode
CROSS APPLY
(
    SELECT TOP (1) c.name
    FROM sys.columns c
    WHERE c.object_id = T.object_id
      AND UPPER(c.name) IN ('DEPARTMENTNAME','DEPTNAME','TEN_KHOA','KHOANAME','NAME')
) CName
ORDER BY
    CASE WHEN T.TableName LIKE '%Department%' THEN 0 ELSE 1 END,
    T.TableName;

IF @DepartmentTable IS NOT NULL
BEGIN
    DECLARE @SqlDepartment nvarchar(max) =
        N'INSERT INTO #DEPARTMENT_LOOKUP(Code, Name)
          SELECT DISTINCT
              CONVERT(nvarchar(255), ' + QUOTENAME(@DepartmentCodeCol) + N'),
              CONVERT(nvarchar(500), ' + QUOTENAME(@DepartmentNameCol) + N')
          FROM ' + @DepartmentTable + N'
          WHERE ' + QUOTENAME(@DepartmentCodeCol) + N' IS NOT NULL;';

    EXEC sp_executesql @SqlDepartment;
END;

WITH XML1_SOURCE AS
(
    SELECT
        rb.MA_LK,
        rb.PatientCode AS MA_BN,
        p.PatientName AS HO_TEN,
        p.PatientBirthday AS NGAY_SINH,
        CASE
            WHEN p.PatientGender = 0 THEN N'Nam'
            WHEN p.PatientGender = 1 THEN N'Nữ'
            ELSE CONVERT(nvarchar(20), p.PatientGender)
        END AS GIOI_TINH,

        rb.Serial AS BHYT,

        COALESCE(
            NULLIF(LTRIM(RTRIM(rb.PatientAddress)), ''),
            NULLIF(LTRIM(RTRIM(pr.Address)), '')
        ) AS DIA_CHI,

        pr.CMND AS SO_CCCD,
        rb.ProvincialCode AS MA_TINH,
        rb.DistrictCode AS MA_HUYEN,
        rb.WardCode AS MA_XA,

        rb.ICD10_Custom AS ICD10,
        rb.ICD10_KT AS ICD10KT,
        rb.DiagnosisCustom AS CHAN_DOAN_RV,

        CASE WHEN rb.DateInto IS NULL THEN ''
             ELSE CONVERT(varchar(10), rb.DateInto, 103) + ' '
                  + LEFT(CONVERT(varchar(8), rb.DateInto, 108), 5)
        END AS NGAY_VAO,

        CASE WHEN rb.DateOut IS NULL THEN ''
             ELSE CONVERT(varchar(10), rb.DateOut, 103) + ' '
                  + LEFT(CONVERT(varchar(8), rb.DateOut, 108), 5)
        END AS NGAY_RA,

        CASE WHEN rb.PostingDate IS NULL THEN ''
             ELSE CONVERT(varchar(10), rb.PostingDate, 103) + ' '
                  + LEFT(CONVERT(varchar(8), rb.PostingDate, 108), 5)
        END AS NGAY_TTOAN,

        CASE WHEN rb.PostingDate IS NULL THEN ''
             ELSE RIGHT('0' + CONVERT(varchar(2), MONTH(rb.PostingDate)), 2)
                  + '/' + CONVERT(varchar(4), YEAR(rb.PostingDate))
        END AS THANG_QT,

        rb.DepartmentCode AS MA_KHOA,
        rb.EmployeeCodeDoctor AS MA_BAC_SI,
        rb.TotalAmount AS T_TONGCHI_BV,
        rb.TongBH AS T_TONGCHI_BH,
        rb.BHYTPay AS T_BHTT,
        rb.BNTraBH AS TBNTT,
        rb.T_BNTT,
        rb.PatientReceiveID,
        rb.ReportID,

        ROW_NUMBER() OVER
        (
            PARTITION BY rb.MA_LK
            ORDER BY
                CASE WHEN rb.Cancel = 0 THEN 0 ELSE 1 END,
                rb.PostingDate DESC,
                rb.ReportID DESC
        ) AS RN
    FROM dbo.ReportBHYT rb
    INNER JOIN dbo.PatientReceive pr
        ON pr.PatientReceiveID = rb.PatientReceiveID
    LEFT JOIN dbo.Patients p
        ON p.PatientCode = rb.PatientCode
    WHERE
        (@MaLK IS NULL OR rb.MA_LK = @MaLK)
        AND rb.PostingDate >= @FromDate
        AND rb.PostingDate < DATEADD(day, 1, @ToDate)
        AND rb.Cancel = 0
)
SELECT
    MA_LK,
    MA_BN,
    HO_TEN,
    NGAY_SINH,
    GIOI_TINH,
    BHYT,
    DIA_CHI,
    SO_CCCD,
    MA_TINH,
    MA_HUYEN,
    MA_XA,
    ICD10,
    ICD10KT,
    CHAN_DOAN_RV,
    NGAY_VAO,
    NGAY_RA,
    NGAY_TTOAN,
    THANG_QT,
    MA_KHOA,
    MA_BAC_SI,
    T_TONGCHI_BV,
    T_TONGCHI_BH,
    T_BHTT,
    TBNTT,
    T_BNTT,
    PatientReceiveID,
    ReportID
INTO #XML1_SELECTED
FROM XML1_SOURCE
WHERE RN = 1;

SELECT
    MA_LK,
    MA_BN,
    HO_TEN,
    NGAY_SINH,
    GIOI_TINH,
    BHYT,
    DIA_CHI,
    SO_CCCD,
    MA_TINH,
    MA_HUYEN,
    MA_XA,
    ICD10,
    ICD10KT,
    CHAN_DOAN_RV,
    NGAY_VAO,
    NGAY_RA,
    NGAY_TTOAN,
    THANG_QT,
    COALESCE(dl.Name, MA_KHOA) AS TEN_KHOA,
    COALESCE(dr.Name, MA_BAC_SI) AS TEN_BAC_SI,
    T_TONGCHI_BV,
    T_TONGCHI_BH,
    T_BHTT,
    TBNTT,
    T_BNTT,
    PatientReceiveID,
    ReportID
FROM #XML1_SELECTED x
LEFT JOIN #DEPARTMENT_LOOKUP dl
    ON dl.Code = CONVERT(nvarchar(255), x.MA_KHOA)
LEFT JOIN #DOCTOR_LOOKUP dr
    ON dr.Code = CONVERT(nvarchar(255), x.MA_BAC_SI)
ORDER BY NGAY_TTOAN, MA_LK;

SELECT
    x1.MA_LK,
    x1.MA_BN,
    x1.HO_TEN,
    x1.NGAY_SINH,
    x1.BHYT,

    d.Ordinal AS STT,

    COALESCE(
        NULLIF(ipk.MaBYT_PK, ''),
        NULLIF(d.ServiceCode_PK, ''),
        NULLIF(i.MaBYT_PK, ''),
        NULLIF(i.ItemCode_PK, ''),
        d.ServiceCode
    ) AS MA_THUOC,

    COALESCE(
        NULLIF(ipk.ItemName_BYT_XML, ''),
        NULLIF(i.ItemName_KT, ''),
        i.ItemName
    ) AS TEN_THUOC,

    i.Active AS HOAT_CHAT,
    i.UnitOfMeasureCode AS DON_VI_TINH,

    COALESCE(
        NULLIF(ipk.UsageCode_BYT_XML, ''),
        NULLIF(i.UsageCode, '')
    ) AS DUONG_DUNG,

    COALESCE(
        NULLIF(d.Lieu_Dung_XML, ''),
        NULLIF(d.Instruction, '')
    ) AS LIEU_DUNG,

    d.Instruction AS CACH_DUNG,

    COALESCE(
        NULLIF(ipk.SODKGP, ''),
        NULLIF(d.SODKGP, '')
    ) AS SO_DANG_KY,

    d.Quantity AS SO_LUONG,
    d.BHYTPrice AS DON_GIA_BH,
    d.ServicePrice AS DON_GIA_BV,
    d.Amount AS THANH_TIEN_BV,
    d.BHYTPay AS T_BHTT,
    d.PatientPay AS T_BNTT,
    COALESCE(dr2.Name, d.MaCCHN) AS TEN_BAC_SI,

    CASE WHEN d.NGAY_YL IS NULL THEN ''
         ELSE CONVERT(varchar(10), d.NGAY_YL, 103) + ' '
              + LEFT(CONVERT(varchar(8), d.NGAY_YL, 108), 5)
    END AS NGAY_YL,

    CASE WHEN d.NGAY_TH_YL IS NULL THEN ''
         ELSE CONVERT(varchar(10), d.NGAY_TH_YL, 103) + ' '
              + LEFT(CONVERT(varchar(8), d.NGAY_TH_YL, 108), 5)
    END AS NGAY_TH_YL,

    CASE WHEN d.NGAY_KQ IS NULL THEN ''
         ELSE CONVERT(varchar(10), d.NGAY_KQ, 103) + ' '
              + LEFT(CONVERT(varchar(8), d.NGAY_KQ, 108), 5)
    END AS NGAY_KQ,

    d.ReportID,
    d.SuggestedID

FROM #XML1_SELECTED x1

INNER JOIN dbo.ReportBHYTDetail d
    ON d.ReportID = x1.ReportID

INNER JOIN dbo.Items i
    ON i.ItemCode = d.ServiceCode

LEFT JOIN #DOCTOR_LOOKUP dr2
    ON dr2.Code = CONVERT(nvarchar(255), d.MaCCHN)

OUTER APPLY
(
    SELECT TOP (1)
        x.MaBYT_PK,
        x.ItemName_BYT_XML,
        x.UsageCode_BYT_XML,
        x.SODKGP
    FROM dbo.Items_BHYT_PK x
    WHERE
        x.ItemCode_PK = COALESCE(
            NULLIF(d.ServiceCode_PK, ''),
            NULLIF(i.ItemCode_PK, '')
        )
        AND ISNULL(x.Hide, 0) = 0
    ORDER BY
        ISNULL(x.Hide_BV, 0),
        x.IDate DESC,
        x.RowID DESC
) ipk



ORDER BY
    x1.MA_LK,
    x1.ReportID,
    d.Ordinal;

SELECT
    x1.MA_LK,
    x1.MA_BN,
    x1.HO_TEN,
    x1.NGAY_SINH,
    x1.BHYT,

    d.Ordinal AS STT,

    COALESCE(
        NULLIF(s.MaTT50_BHYT, ''),
        d.ServiceCode_PK,
        d.ServiceCode
    ) AS MA_DICH_VU,

    COALESCE(
        NULLIF(s.TenTT50_BHYT, ''),
        s.ServiceName
    ) AS TEN_DICH_VU,

    d.Quantity AS SO_LUONG,
    d.ServicePrice AS DON_GIA_BV,
    d.BHYTPrice AS DON_GIA_BH,
    d.Amount AS THANH_TIEN_BV,
    d.BHYTPay AS T_BHTT,
    d.PatientPay AS T_BNTT,
    COALESCE(dl3.Name, d.DepartmentCode) AS TEN_KHOA,

    COALESCE(
        dr3.Name,
        NULLIF(d.MaCCHN_CD, ''),
        NULLIF(d.MaCCHN, '')
    ) AS TEN_BAC_SI,

    d.EmployeeCode_EKip_CCHN AS NGUOI_THUC_HIEN,
    d.MA_MAY,

    CASE WHEN d.NGAY_YL IS NULL THEN ''
         ELSE CONVERT(varchar(10), d.NGAY_YL, 103) + ' '
              + LEFT(CONVERT(varchar(8), d.NGAY_YL, 108), 5)
    END AS NGAY_YL,

    CASE WHEN d.NGAY_TH_YL IS NULL THEN ''
         ELSE CONVERT(varchar(10), d.NGAY_TH_YL, 103) + ' '
              + LEFT(CONVERT(varchar(8), d.NGAY_TH_YL, 108), 5)
    END AS NGAY_TH_YL,

    CASE WHEN d.NGAY_KQ IS NULL THEN ''
         ELSE CONVERT(varchar(10), d.NGAY_KQ, 103) + ' '
              + LEFT(CONVERT(varchar(8), d.NGAY_KQ, 108), 5)
    END AS NGAY_KQ,

    d.ReportID,
    d.SuggestedID

FROM #XML1_SELECTED x1

INNER JOIN dbo.ReportBHYTDetail d
    ON d.ReportID = x1.ReportID

INNER JOIN dbo.Service s
    ON s.ServiceCode = d.ServiceCode

LEFT JOIN #DEPARTMENT_LOOKUP dl3
    ON dl3.Code = CONVERT(nvarchar(255), d.DepartmentCode)

LEFT JOIN #DOCTOR_LOOKUP dr3
    ON dr3.Code = CONVERT(
        nvarchar(255),
        COALESCE(
            NULLIF(d.MaCCHN_CD, ''),
            NULLIF(d.MaCCHN, '')
        )
    )

ORDER BY
    x1.MA_LK,
    x1.ReportID,
    d.Ordinal;
""";

    private const string Xml1Sql = """
WITH XML1_SOURCE AS
(
    SELECT
        rb.MA_LK,
        rb.PatientCode AS MA_BN,
        p.PatientName AS HO_TEN,
        p.PatientBirthday AS NGAY_SINH,
        CASE
            WHEN p.PatientGender = 0 THEN 1
            WHEN p.PatientGender = 1 THEN 2
            ELSE p.PatientGender
        END AS GIOI_TINH,

        rb.Serial AS BHYT,

        COALESCE(
            NULLIF(LTRIM(RTRIM(rb.PatientAddress)), ''),
            NULLIF(LTRIM(RTRIM(pr.Address)), '')
        ) AS DIA_CHI,

        pr.CMND AS SO_CCCD,
        rb.ProvincialCode AS MA_TINH,
        rb.DistrictCode AS MA_HUYEN,
        rb.WardCode AS MA_XA,

        rb.ICD10_Custom AS ICD10,
        rb.ICD10_KT AS ICD10KT,
        rb.DiagnosisCustom AS CHAN_DOAN_RV,

        CASE WHEN rb.DateInto IS NULL THEN ''
             ELSE CONVERT(varchar(10), rb.DateInto, 103) + ' '
                  + LEFT(CONVERT(varchar(8), rb.DateInto, 108), 5)
        END AS NGAY_VAO,

        CASE WHEN rb.DateOut IS NULL THEN ''
             ELSE CONVERT(varchar(10), rb.DateOut, 103) + ' '
                  + LEFT(CONVERT(varchar(8), rb.DateOut, 108), 5)
        END AS NGAY_RA,

        CASE WHEN rb.PostingDate IS NULL THEN ''
             ELSE CONVERT(varchar(10), rb.PostingDate, 103) + ' '
                  + LEFT(CONVERT(varchar(8), rb.PostingDate, 108), 5)
        END AS NGAY_TTOAN,

        rb.DepartmentCode AS MA_KHOA,
        rb.EmployeeCodeDoctor AS MA_BAC_SI,
        rb.TotalAmount AS T_TONGCHI_BV,
        rb.TongBH AS T_TONGCHI_BH,
        rb.BHYTPay AS T_BHTT,
        rb.BNTraBH AS TBNTT,
        rb.T_BNTT,
        rb.PatientReceiveID,
        rb.ReportID,

        ROW_NUMBER() OVER
        (
            PARTITION BY rb.MA_LK
            ORDER BY
                CASE WHEN rb.Cancel = 0 THEN 0 ELSE 1 END,
                rb.PostingDate DESC,
                rb.ReportID DESC
        ) AS RN
    FROM dbo.ReportBHYT rb
    INNER JOIN dbo.PatientReceive pr
        ON pr.PatientReceiveID = rb.PatientReceiveID
    LEFT JOIN dbo.Patients p
        ON p.PatientCode = rb.PatientCode
    WHERE
        (@MaLK IS NULL OR rb.MA_LK = @MaLK)
        AND rb.PostingDate >= @FromDate
        AND rb.PostingDate < DATEADD(day, 1, @ToDate)
        AND rb.Cancel = 0
)
SELECT
    MA_LK,
    MA_BN,
    HO_TEN,
    NGAY_SINH,
    GIOI_TINH,
    BHYT,
    DIA_CHI,
    SO_CCCD,
    MA_TINH,
    MA_HUYEN,
    MA_XA,
    ICD10,
    ICD10KT,
    CHAN_DOAN_RV,
    NGAY_VAO,
    NGAY_RA,
    NGAY_TTOAN,
    MA_KHOA,
    MA_BAC_SI,
    T_TONGCHI_BV,
    T_TONGCHI_BH,
    T_BHTT,
    TBNTT,
    T_BNTT,
    PatientReceiveID,
    ReportID
FROM XML1_SOURCE
WHERE RN = 1
ORDER BY NGAY_TTOAN, MA_LK;
""";

    private const string Xml2Sql = """
WITH XML1_DATA AS
(
    SELECT
        rb.ReportID,
        rb.MA_LK,
        rb.PatientCode,
        rb.Serial,
        rb.PostingDate,
        ROW_NUMBER() OVER
        (
            PARTITION BY rb.MA_LK
            ORDER BY
                CASE WHEN rb.Cancel = 0 THEN 0 ELSE 1 END,
                rb.PostingDate DESC,
                rb.ReportID DESC
        ) AS RN
    FROM dbo.ReportBHYT rb
    WHERE
        (@MaLK IS NULL OR rb.MA_LK = @MaLK)
        AND rb.PostingDate >= @FromDate
        AND rb.PostingDate < DATEADD(day, 1, @ToDate)
),
XML1_SELECTED AS
(
    SELECT
        ReportID,
        MA_LK,
        PatientCode,
        Serial
    FROM XML1_DATA
    WHERE RN = 1
)
SELECT
    x1.MA_LK,
    x1.PatientCode AS MA_BN,
    p.PatientName AS HO_TEN,
    p.PatientBirthday AS NGAY_SINH,
    x1.Serial AS BHYT,

    d.Ordinal AS STT,

    COALESCE(
        NULLIF(ipk.MaBYT_PK, ''),
        NULLIF(d.ServiceCode_PK, ''),
        NULLIF(i.MaBYT_PK, ''),
        NULLIF(i.ItemCode_PK, ''),
        d.ServiceCode
    ) AS MA_THUOC,

    COALESCE(
        NULLIF(ipk.ItemName_BYT_XML, ''),
        NULLIF(i.ItemName_KT, ''),
        i.ItemName
    ) AS TEN_THUOC,

    i.Active AS HOAT_CHAT,
    i.UnitOfMeasureCode AS DON_VI_TINH,

    COALESCE(
        NULLIF(ipk.UsageCode_BYT_XML, ''),
        NULLIF(i.UsageCode, '')
    ) AS DUONG_DUNG,

    COALESCE(
        NULLIF(d.Lieu_Dung_XML, ''),
        NULLIF(d.Instruction, '')
    ) AS LIEU_DUNG,

    d.Instruction AS CACH_DUNG,

    COALESCE(
        NULLIF(ipk.SODKGP, ''),
        NULLIF(d.SODKGP, '')
    ) AS SO_DANG_KY,

    d.Quantity AS SO_LUONG,
    d.BHYTPrice AS DON_GIA_BH,
    d.ServicePrice AS DON_GIA_BV,
    d.Amount AS THANH_TIEN_BV,
    d.BHYTPay AS T_BHTT,
    d.PatientPay AS T_BNTT,
    d.MaCCHN AS MA_BAC_SI,

    CASE WHEN d.NGAY_YL IS NULL THEN ''
         ELSE CONVERT(varchar(10), d.NGAY_YL, 103) + ' '
              + LEFT(CONVERT(varchar(8), d.NGAY_YL, 108), 5)
    END AS NGAY_YL,

    CASE WHEN d.NGAY_TH_YL IS NULL THEN ''
         ELSE CONVERT(varchar(10), d.NGAY_TH_YL, 103) + ' '
              + LEFT(CONVERT(varchar(8), d.NGAY_TH_YL, 108), 5)
    END AS NGAY_TH_YL,

    CASE WHEN d.NGAY_KQ IS NULL THEN ''
         ELSE CONVERT(varchar(10), d.NGAY_KQ, 103) + ' '
              + LEFT(CONVERT(varchar(8), d.NGAY_KQ, 108), 5)
    END AS NGAY_KQ,

    d.ReportID,
    d.SuggestedID

FROM XML1_SELECTED x1

INNER JOIN dbo.ReportBHYTDetail d
    ON d.ReportID = x1.ReportID

INNER JOIN dbo.Items i
    ON i.ItemCode = d.ServiceCode

OUTER APPLY
(
    SELECT TOP (1)
        x.MaBYT_PK,
        x.ItemName_BYT_XML,
        x.UsageCode_BYT_XML,
        x.SODKGP
    FROM dbo.Items_BHYT_PK x
    WHERE
        x.ItemCode_PK = COALESCE(
            NULLIF(d.ServiceCode_PK, ''),
            NULLIF(i.ItemCode_PK, '')
        )
        AND ISNULL(x.Hide, 0) = 0
    ORDER BY
        ISNULL(x.Hide_BV, 0),
        x.IDate DESC,
        x.RowID DESC
) ipk

LEFT JOIN dbo.Patients p
    ON p.PatientCode = x1.PatientCode

ORDER BY
    x1.MA_LK,
    x1.ReportID,
    d.Ordinal;
""";

    private const string Xml3Sql = """
WITH XML1_DATA AS
(
    SELECT
        rb.ReportID,
        rb.MA_LK,
        rb.PatientCode,
        rb.Serial,
        rb.PostingDate,
        ROW_NUMBER() OVER
        (
            PARTITION BY rb.MA_LK
            ORDER BY
                CASE WHEN rb.Cancel = 0 THEN 0 ELSE 1 END,
                rb.PostingDate DESC,
                rb.ReportID DESC
        ) AS RN
    FROM dbo.ReportBHYT rb
    WHERE
        (@MaLK IS NULL OR rb.MA_LK = @MaLK)
        AND rb.PostingDate >= @FromDate
        AND rb.PostingDate < DATEADD(day, 1, @ToDate)
),
XML1_SELECTED AS
(
    SELECT
        ReportID,
        MA_LK,
        PatientCode,
        Serial
    FROM XML1_DATA
    WHERE RN = 1
)
SELECT
    x1.MA_LK,
    x1.PatientCode AS MA_BN,
    p.PatientName AS HO_TEN,
    p.PatientBirthday AS NGAY_SINH,
    x1.Serial AS BHYT,

    d.Ordinal AS STT,

    COALESCE(
        NULLIF(s.MaTT50_BHYT, ''),
        d.ServiceCode_PK,
        d.ServiceCode
    ) AS MA_DICH_VU,

    COALESCE(
        NULLIF(s.TenTT50_BHYT, ''),
        s.ServiceName
    ) AS TEN_DICH_VU,

    d.Quantity AS SO_LUONG,
    d.ServicePrice AS DON_GIA_BV,
    d.BHYTPrice AS DON_GIA_BH,
    d.Amount AS THANH_TIEN_BV,
    d.BHYTPay AS T_BHTT,
    d.PatientPay AS T_BNTT,
    d.DepartmentCode AS MA_KHOA,

    COALESCE(
        NULLIF(d.MaCCHN_CD, ''),
        NULLIF(d.MaCCHN, '')
    ) AS MA_BAC_SI,

    d.EmployeeCode_EKip_CCHN AS NGUOI_THUC_HIEN,
    d.MA_MAY,

    CASE WHEN d.NGAY_YL IS NULL THEN ''
         ELSE CONVERT(varchar(10), d.NGAY_YL, 103) + ' '
              + LEFT(CONVERT(varchar(8), d.NGAY_YL, 108), 5)
    END AS NGAY_YL,

    CASE WHEN d.NGAY_TH_YL IS NULL THEN ''
         ELSE CONVERT(varchar(10), d.NGAY_TH_YL, 103) + ' '
              + LEFT(CONVERT(varchar(8), d.NGAY_TH_YL, 108), 5)
    END AS NGAY_TH_YL,

    CASE WHEN d.NGAY_KQ IS NULL THEN ''
         ELSE CONVERT(varchar(10), d.NGAY_KQ, 103) + ' '
              + LEFT(CONVERT(varchar(8), d.NGAY_KQ, 108), 5)
    END AS NGAY_KQ,

    d.ReportID,
    d.SuggestedID

FROM XML1_SELECTED x1

INNER JOIN dbo.ReportBHYTDetail d
    ON d.ReportID = x1.ReportID

INNER JOIN dbo.Service s
    ON s.ServiceCode = d.ServiceCode

LEFT JOIN dbo.Patients p
    ON p.PatientCode = x1.PatientCode

ORDER BY
    x1.MA_LK,
    x1.ReportID,
    d.Ordinal;
""";

}