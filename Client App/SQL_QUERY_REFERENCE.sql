-- Kiến trúc chính:
-- PatientReceive -> SuggestedServiceReceipt -> ServicePackage
--
-- Một PatientReceive = một hồ sơ trên TableView.
-- CROSS APPLY TOP (1) dùng để lấy dòng gói KSK LÁI XE phù hợp,
-- tránh một lượt tiếp nhận bị lặp thành nhiều dòng.

DECLARE @DateFrom date = CAST(GETDATE() AS date);
DECLARE @OnlyPaid bit = 0;

SELECT
    ksk.ServicePackageName AS ServiceName,
    ksk.CreateDate,
    ksk.Paid,

    LTRIM(RTRIM(
        CASE
            WHEN CHARINDEX('-', REVERSE(ISNULL(pr.Reason, ''))) > 0
            THEN LEFT(pr.Reason, LEN(pr.Reason) - CHARINDEX('-', REVERSE(pr.Reason)))
            ELSE ISNULL(pr.Reason, '')
        END
    )) AS SO,

    LTRIM(RTRIM(
        CASE
            WHEN CHARINDEX('-', REVERSE(ISNULL(pr.Reason, ''))) > 0
            THEN RIGHT(pr.Reason, CHARINDEX('-', REVERSE(pr.Reason)) - 1)
            ELSE ''
        END
    )) AS HANGBANGLAI,

    p.PatientName AS HOTEN,
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

    '' AS NONGDOCON,
    '' AS DVINONGDOCON,
    '0' AS MATUY,
    CONVERT(varchar(10), ksk.CreateDate, 103) AS NGAYKETLUAN,
    ci.Doctor AS BACSYKETLUAN,
    'A0-1' AS KETLUAN,
    '' AS LYDO,
    '' AS TINHTRANGBENH,
    'ADD' AS STATE,
    '' AS SIGNDATA

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
      AND sp.ServicePackageName LIKE N'%KSK LÁI XE%'
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
  AND pr.CreateDate < DATEADD(day, 1, @DateFrom)

ORDER BY pr.CreateDate DESC, pr.PatientReceiveID DESC;
