using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;

namespace KSKSigningServer.Services;

public static class Pkcs11XmlSigner
{
    public static void Sign(
        XmlDocument document,
        Pkcs11TokenService token)
    {
        var certificate = token.Certificate;

        using var rsa = new Pkcs11Rsa(token, certificate);
        var signedXml = new SignedXml(document)
        {
            SigningKey = rsa
        };

        var signedInfo = signedXml.SignedInfo
            ?? throw new CryptographicException("Không thể khởi tạo thông tin chữ ký XML.");
        signedInfo.CanonicalizationMethod =
            SignedXml.XmlDsigCanonicalizationUrl;
        signedInfo.SignatureMethod =
            SignedXml.XmlDsigRSASHA256Url;

        var reference = new Reference("")
        {
            DigestMethod = SignedXml.XmlDsigSHA256Url
        };
        reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        reference.AddTransform(new XmlDsigC14NTransform());
        signedXml.AddReference(reference);

        var keyInfo = new KeyInfo();
        keyInfo.AddClause(
            new KeyInfoX509Data(
                certificate,
                X509IncludeOption.EndCertOnly));
        signedXml.KeyInfo = keyInfo;

        signedXml.ComputeSignature();
        var signature = signedXml.GetXml();

        document.DocumentElement?.AppendChild(
            document.ImportNode(signature, true));
    }
}

internal sealed class Pkcs11Rsa : RSA
{
    private readonly Pkcs11TokenService _token;
    private readonly RSA _publicRsa;

    public Pkcs11Rsa(
        Pkcs11TokenService token,
        System.Security.Cryptography.X509Certificates.X509Certificate2 cert)
    {
        _token = token;
        _publicRsa = cert.GetRSAPublicKey()
                     ?? throw new CryptographicException(
                         "Chứng thư trong Token không phải RSA.");
        KeySizeValue = _publicRsa.KeySize;
    }

    public override byte[] SignHash(
        byte[] hash,
        HashAlgorithmName hashAlgorithm,
        RSASignaturePadding padding)
    {
        if (padding != RSASignaturePadding.Pkcs1)
            throw new NotSupportedException(
                "PKCS#11 engine chỉ hỗ trợ RSA PKCS#1 v1.5.");

        return _token.SignHash(hash, hashAlgorithm);
    }

    public override bool VerifyHash(
        byte[] hash,
        byte[] signature,
        HashAlgorithmName hashAlgorithm,
        RSASignaturePadding padding) =>
        _publicRsa.VerifyHash(
            hash,
            signature,
            hashAlgorithm,
            padding);

    public override RSAParameters ExportParameters(
        bool includePrivateParameters) =>
        _publicRsa.ExportParameters(false);

    public override void ImportParameters(RSAParameters parameters) =>
        throw new NotSupportedException();

    public override byte[] Decrypt(
        byte[] data,
        RSAEncryptionPadding padding) =>
        throw new NotSupportedException();

    public override byte[] Encrypt(
        byte[] data,
        RSAEncryptionPadding padding) =>
        _publicRsa.Encrypt(data, padding);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _publicRsa.Dispose();

        base.Dispose(disposing);
    }
}
