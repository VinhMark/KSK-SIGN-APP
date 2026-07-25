using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using GKSKLaiXe.Models;

namespace GKSKLaiXe.Services;

public sealed class SignDataService
{
    public X509Certificate2? SelectCertificate()
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);

        var valid = store.Certificates
            .Find(X509FindType.FindByTimeValid, DateTime.Now, false)
            .Cast<X509Certificate2>()
            .Where(c => c.HasPrivateKey)
            .ToArray();

        if (valid.Length == 0)
            return null;

        var selected = X509Certificate2UI.SelectFromCollection(
            new X509Certificate2Collection(valid),
            "Chọn chứng thư số",
            "Chọn chứng thư số dùng để ký dữ liệu giấy khám sức khỏe.",
            X509SelectionFlag.SingleSelection);

        return selected.Count > 0 ? selected[0] : null;
    }

    public string CreateSignedBase64(GkskRecord r, X509Certificate2 certificate)
    {
        var signedXmlText = CreateSignedXmlText(r, certificate);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(signedXmlText));
    }

    public string CreateUnsignedXmlText(GkskRecord r)
    {
        var xml = BuildUnsignedXml(r);
        return ToUtf8Xml(xml);
    }

    public string CreateSignedXmlText(GkskRecord r, X509Certificate2 certificate)
    {
        if (!certificate.HasPrivateKey)
            throw new InvalidOperationException("Chứng thư số không có private key.");

        var xml = BuildUnsignedXml(r);
        SignXml(xml, certificate);
        return ToUtf8Xml(xml);
    }


    public string CreateApiPayloadXmlText(
        GkskRecord r,
        bool sign,
        X509Certificate2? certificate)
    {
        string signData;

        if (sign)
        {
            if (certificate is null)
                throw new InvalidOperationException("Chưa có chứng thư số để ký.");

            // Ký trực tiếp XML <root>, thẻ <Signature> được nhúng bên trong <root>.
            // Sau đó Base64 toàn bộ XML <root> đã ký để đưa vào SIGNDATA.
            var signedRootXmlText = CreateSignedXmlText(r, certificate);
            signData = Convert.ToBase64String(
                Encoding.UTF8.GetBytes(signedRootXmlText));
        }
        else
        {
            signData = "test";
        }

        r.SIGNDATA = signData;

        var doc = new XmlDocument
        {
            PreserveWhitespace = true
        };

        var declaration = doc.CreateXmlDeclaration("1.0", "UTF-8", null);
        doc.AppendChild(declaration);

        var root = doc.CreateElement("GKSK");
        doc.AppendChild(root);

        Add(root, "SO", r.SO);
        Add(root, "HOTEN", r.HOTEN);
        Add(root, "GIOITINHVAL", r.GIOITINHVAL);
        Add(root, "NGAYSINH", r.NGAYSINH);
        Add(root, "DIACHITHUONGTRU", r.DIACHITHUONGTRU);
        Add(root, "MATINH_THUONGTRU", r.MATINH_THUONGTRU);
        Add(root, "MAXA_THUONGTRU", r.MAXA_THUONGTRU);
        Add(root, "SOCMND_PASSPORT", r.SOCMND_PASSPORT);
        Add(root, "NGAYTHANGNAMCAPCMND", r.NGAYTHANGNAMCAPCMND);
        Add(root, "NOICAP", r.NOICAP);
        Add(root, "IDBENHVIEN", r.IDBENHVIEN);
        Add(root, "BENHVIEN", r.BENHVIEN);
        Add(root, "NONGDOCON", r.NONGDOCON);
        Add(root, "DVINONGDOCON", r.DVINONGDOCON);
        Add(root, "MATUY", r.MATUY);
        Add(root, "NGAYKETLUAN", r.NGAYKETLUAN);
        Add(root, "BACSYKETLUAN", r.BACSYKETLUAN);
        Add(root, "KETLUAN", r.KETLUAN);
        Add(root, "HANGBANGLAI", r.HANGBANGLAI);
        Add(root, "LYDO", r.LYDO);
        Add(root, "TINHTRANGBENH", r.TINHTRANGBENH);
        Add(root, "STATE", r.STATE);
        Add(root, "SIGNDATA", signData);

        return ToUtf8Xml(doc);
    }

    private static XmlDocument BuildUnsignedXml(GkskRecord r)
    {
        var doc = new XmlDocument
        {
            PreserveWhitespace = true
        };

        var declaration = doc.CreateXmlDeclaration("1.0", "UTF-8", null);
        doc.AppendChild(declaration);

        var root = doc.CreateElement("root");
        doc.AppendChild(root);

        Add(root, "UUID", "");
        Add(root, "CREATEDDATE", "");
        Add(root, "USERCREATE", "");
        Add(root, "STATUS", "");
        Add(root, "ACTION", "");
        Add(root, "SO", r.SO);
        Add(root, "HOTEN", r.HOTEN);
        Add(root, "NGAYSINH", r.NGAYSINH);
        Add(root, "GIOITINHVAL", r.GIOITINHVAL);
        Add(root, "SOCMND_PASSPORT", r.SOCMND_PASSPORT);
        Add(root, "NGAYTHANGNAMCAPCMD", r.NGAYTHANGNAMCAPCMND);
        Add(root, "NOICAP", r.NOICAP);
        Add(root, "ECITIZENCODE", "");
        Add(root, "MOBILE", "");
        Add(root, "EMAIL", "");
        Add(root, "DIACHITHUONGTRU", r.DIACHITHUONGTRU);
        Add(root, "MATINH_THUONGTRU", r.MATINH_THUONGTRU);
        Add(root, "MAHUYEN_THUONGTRU", "");
        Add(root, "MAXA_THUONGTRU", r.MAXA_THUONGTRU);
        Add(root, "NONGDOCON", r.NONGDOCON);
        Add(root, "DVINONGDOCON", r.DVINONGDOCON);
        Add(root, "MATUY", r.MATUY);
        Add(root, "KETLUAN", r.KETLUAN);
        Add(root, "HANGBANGLAI", r.HANGBANGLAI);
        Add(root, "NGAYKETLUAN", r.NGAYKETLUAN);
        Add(root, "BACSYKETLUAN", r.BACSYKETLUAN);
        Add(root, "NGAYKHAMLAI", "");
        Add(root, "LYDO", r.LYDO);
        Add(root, "TINHTRANGBENH", r.TINHTRANGBENH);

        return doc;
    }

    private static void SignXml(XmlDocument doc, X509Certificate2 certificate)
    {
        using RSA? privateKey = certificate.GetRSAPrivateKey();
        if (privateKey is null)
            throw new InvalidOperationException("Không lấy được RSA private key từ chứng thư số.");

        var signedXml = new SignedXml(doc)
        {
            SigningKey = privateKey
        };

        // Theo tài liệu: RSA-SHA1, digest SHA1, Reference URI rỗng.
        var signedInfo = signedXml.SignedInfo
            ?? throw new InvalidOperationException("Không khởi tạo được SignedInfo.");

        signedInfo.CanonicalizationMethod =
            SignedXml.XmlDsigCanonicalizationUrl;

        signedInfo.SignatureMethod =
            SignedXml.XmlDsigRSASHA1Url;

        var reference = new Reference
        {
            Uri = "",
            DigestMethod = SignedXml.XmlDsigSHA1Url
        };

        reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        signedXml.AddReference(reference);

        var keyInfo = new KeyInfo();
        keyInfo.AddClause(new RSAKeyValue(privateKey));

        var x509Data = new KeyInfoX509Data(certificate);
        x509Data.AddCertificate(certificate);
        keyInfo.AddClause(x509Data);

        signedXml.KeyInfo = keyInfo;

        // Thêm SigningTime theo cấu trúc tài liệu.
        var obj = new DataObject();
        var objectDoc = new XmlDocument();
        var props = objectDoc.CreateElement("SignatureProperties");

        var prop = objectDoc.CreateElement("SignatureProperty");
        prop.SetAttribute("Target", "signatureProperties");
        prop.SetAttribute("Id", "SigningTime");

        var time = objectDoc.CreateElement("SigningTime");
        time.InnerText = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        prop.AppendChild(time);
        props.AppendChild(prop);

        obj.Data = props.SelectNodes(".");
        signedXml.AddObject(obj);

        signedXml.ComputeSignature();

        XmlElement signature = signedXml.GetXml();
        doc.DocumentElement!.AppendChild(doc.ImportNode(signature, true));
    }

    private static void Add(XmlElement parent, string name, string? value)
    {
        var doc = parent.OwnerDocument!;
        var element = doc.CreateElement(name);
        element.InnerText = value ?? "";
        parent.AppendChild(element);
    }

    private static string ToUtf8Xml(XmlDocument doc)
    {
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = false,
            OmitXmlDeclaration = false
        };

        using var ms = new MemoryStream();
        using (var writer = XmlWriter.Create(ms, settings))
        {
            doc.Save(writer);
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }
}
