using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using WovenBackend.Services.Security;

namespace WovenBackend.Tests;

public class EncryptionContractTests
{
    private static EncryptionService Service(byte keyByte = 7) => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        { ["Encryption:MasterKey"] = Convert.ToBase64String(Enumerable.Repeat(keyByte, 32).ToArray()) }).Build());

    [Theory]
    [InlineData("")]
    [InlineData("Synthetic private note")]
    [InlineData("\u2764\ufe0f తెలుగు\nline two\u0000")]
    public void TextRoundTrip_PreservesExactInput(string text) => Assert.Equal(text, Service().Decrypt(Service().Encrypt(text)));

    [Fact]
    public void BinaryRoundTrip_PreservesAllByteValues()
    {
        var data = Enumerable.Range(0, 256).Select(n => (byte)n).ToArray();
        Assert.Equal(data, Service().DecryptBytes(Service().EncryptBytes(data)));
    }

    [Fact]
    public void SamePlaintext_UsesDistinctCiphertexts()
    {
        var service = Service();
        Assert.NotEqual(service.Encrypt("Synthetic note"), service.Encrypt("Synthetic note"));
    }

    [Fact]
    public void DifferentMasterKey_CannotDecrypt()
    {
        var encrypted = Service().Encrypt("Synthetic note");
        Assert.ThrowsAny<CryptographicException>(() => Service(8).Decrypt(encrypted));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    [InlineData(28)]
    public void TamperedNonceCiphertextOrTag_IsRejected(int position)
    {
        var encrypted = Service().EncryptBytes(new byte[] { 1, 2, 3 });
        encrypted[position] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => Service().DecryptBytes(encrypted));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    [InlineData(27)]
    public void TruncatedCiphertext_IsRejected(int length) => Assert.ThrowsAny<CryptographicException>(() => Service().DecryptBytes(new byte[length]));

    [Theory]
    [InlineData(null, typeof(InvalidOperationException))]
    [InlineData("invalid base64!", typeof(FormatException))]
    [InlineData("AA==", typeof(InvalidOperationException))]
    public void MissingMalformedOrWrongLengthMasterKey_FailsConfiguration(string? key, Type expectedException)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Encryption:MasterKey"] = key }).Build();
        Assert.Throws(expectedException, () => { _ = new EncryptionService(config); });
    }

    [Fact]
    public void PurposeScopedKeys_AreStableAndDistinct()
    {
        var service = Service();
        Assert.Equal(service.DeriveKey("signing-v1"), Service().DeriveKey("signing-v1"));
        Assert.NotEqual(service.DeriveKey("signing-v1"), service.DeriveKey("cache-encryption-v1"));
        Assert.NotEqual(service.DeriveKey("signing-v1"), Service(8).DeriveKey("signing-v1"));
    }
}
