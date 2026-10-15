using WovenBackend.Services.Security;

namespace WovenBackend.Tests;

public class PiiSanitizerContractTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("We both enjoy walking and reading.")]
    public void TextWithoutIdentifiers_RemainsUnchanged(string input) => Assert.Equal(input, PiiSanitizer.SanitizeForAi(input));

    [Theory]
    [InlineData("Contact synthetic.person@example.invalid", "synthetic.person@example.invalid", "[email]")]
    [InlineData("Email synthetic+tag@example.invalid please", "synthetic+tag@example.invalid", "[email]")]
    [InlineData("Call 555-123-4567", "555-123-4567", "[phone]")]
    [InlineData("Call 555.123.4567", "555.123.4567", "[phone]")]
    [InlineData("Call 555 123 4567", "555 123 4567", "[phone]")]
    [InlineData("Find @synthetic_handle", "@synthetic_handle", "[handle]")]
    [InlineData("Meet at 123 Synthetic Street", "123 Synthetic Street", "their area")]
    [InlineData("Meet at 42 Synthetic rd", "42 Synthetic rd", "their area")]
    public void SupportedIdentifiers_AreRemovedBeforeAiProcessing(string text, string identifier, string replacement)
    {
        var result = PiiSanitizer.SanitizeForAi(text);
        Assert.DoesNotContain(identifier, result);
        Assert.Contains(replacement, result);
        Assert.Equal(result, PiiSanitizer.SanitizeForAi(result));
    }

    [Fact]
    public void CombinedIdentifiers_AreAllRemovedWithoutDiscardingHarmlessContext()
    {
        var result = PiiSanitizer.SanitizeForAi("I enjoy hiking. Email test@example.invalid, call 555-123-4567, follow @synthetic, meet at 123 Synthetic Avenue.");
        Assert.Contains("I enjoy hiking.", result);
        Assert.Contains("[email]", result); Assert.Contains("[phone]", result); Assert.Contains("[handle]", result); Assert.Contains("their area", result);
        Assert.DoesNotContain("example.invalid", result);
    }

    [Fact]
    public void AuditHash_IsStableSaltSensitiveAndNeverTheRawIdentifier()
    {
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", PiiSanitizer.HashForAudit("abc", ""));
        var first = PiiSanitizer.HashForAudit("synthetic", "salt-A");
        Assert.Equal(first, PiiSanitizer.HashForAudit("synthetic", "salt-A"));
        Assert.NotEqual(first, PiiSanitizer.HashForAudit("synthetic", "salt-B"));
        Assert.Matches("^[0-9a-f]{64}$", first);
        Assert.DoesNotContain("synthetic", first);
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 1, 2, 3 })]
    [InlineData(new byte[] { 0x89, 0x50, 0x4e, 0x47 })]
    public void NonJpegContent_IsNotModified(byte[] input) => Assert.Equal(input, PiiSanitizer.StripExif(input));

    [Fact]
    public void JpegMetadataSegment_RemovesApp1AndPreservesOtherSegments()
    {
        byte[] withMetadata = [0xff, 0xd8, 0xff, 0xe1, 0, 4, 10, 11, 0xff, 0xe0, 0, 4, 12, 13, 0xff, 0xd9];
        byte[] expected = [0xff, 0xd8, 0xff, 0xe0, 0, 4, 12, 13, 0xff, 0xd9];
        Assert.Equal(expected, PiiSanitizer.StripExif(withMetadata));
    }

    [Fact]
    public void JpegRestartMarkers_ArePreserved()
    {
        byte[] input = [0xff, 0xd8, 0xff, 0xd0, 0xff, 0xd7, 0xff, 0xd9];
        Assert.Equal(input, PiiSanitizer.StripExif(input));
    }
}
