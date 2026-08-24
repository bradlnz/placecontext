using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;

namespace PlaceContext.Infrastructure.Security;

/// <summary>
/// Encrypts the Data Protection key ring with AES-256-GCM derived from a configured passphrase.
/// Used when <c>PlaceContext:DataProtection:Key</c> is set so a Postgres dump alone cannot decrypt
/// vault secrets or auth cookies. Without the passphrase the ring is stored in plaintext (dev default).
/// </summary>
public sealed class PassphraseXmlEncryptor : IXmlEncryptor, IXmlDecryptor
{
    private readonly byte[] _key;

    // Data Protection records the decryptor type in the persisted key XML and later creates it
    // with Activator.CreateInstance. Keep this constructor parameterless so persisted keys remain
    // readable after a process restart; Kubernetes/.NET configuration exposes the setting through
    // the double-underscore environment variable.
    public PassphraseXmlEncryptor()
        : this(Environment.GetEnvironmentVariable("PlaceContext__DataProtection__Key")
            ?? throw new InvalidOperationException(
                "PlaceContext:DataProtection:Key is required to decrypt the Data Protection key ring."))
    {
    }

    public PassphraseXmlEncryptor(string passphrase)
    {
        // Fixed salt for deterministic derivation from the same passphrase across replicas.
        _key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase),
            "placecontext-dp-v1"u8.ToArray(),
            100_000,
            HashAlgorithmName.SHA256,
            32);
    }

    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        var plain = Encoding.UTF8.GetBytes(plaintextElement.ToString(SaveOptions.DisableFormatting));
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, plain, cipher, tag);

        var el = new XElement("encrypted",
            new XAttribute("alg", "AES-256-GCM"),
            new XElement("nonce", Convert.ToBase64String(nonce)),
            new XElement("tag", Convert.ToBase64String(tag)),
            new XElement("ct", Convert.ToBase64String(cipher)));
        return new EncryptedXmlInfo(el, typeof(PassphraseXmlEncryptor));
    }

    public XElement Decrypt(XElement encryptedElement)
    {
        var nonce = Convert.FromBase64String(encryptedElement.Element("nonce")!.Value);
        var tag = Convert.FromBase64String(encryptedElement.Element("tag")!.Value);
        var cipher = Convert.FromBase64String(encryptedElement.Element("ct")!.Value);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(nonce, cipher, tag, plain);
        return XElement.Parse(Encoding.UTF8.GetString(plain));
    }
}
