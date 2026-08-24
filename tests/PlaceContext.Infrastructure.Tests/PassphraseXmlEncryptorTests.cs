using System.Xml.Linq;
using PlaceContext.Infrastructure.Security;

namespace PlaceContext.Infrastructure.Tests;

public sealed class PassphraseXmlEncryptorTests
{
    [Fact]
    public void Parameterless_decryptor_can_read_a_persisted_key_after_restart()
    {
        const string variable = "PlaceContext__DataProtection__Key";
        const string passphrase = "test-passphrase-with-enough-entropy";
        var previous = Environment.GetEnvironmentVariable(variable);

        try
        {
            Environment.SetEnvironmentVariable(variable, passphrase);
            var encrypted = new PassphraseXmlEncryptor(passphrase)
                .Encrypt(new XElement("key", new XAttribute("id", "test")));

            var activated = Activator.CreateInstance(encrypted.DecryptorType) as PassphraseXmlEncryptor;

            Assert.NotNull(activated);
            Assert.Equal("test", activated.Decrypt(encrypted.EncryptedElement).Attribute("id")?.Value);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }
}
