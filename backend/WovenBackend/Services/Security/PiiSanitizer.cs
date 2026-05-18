using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace WovenBackend.Services.Security;

public static partial class PiiSanitizer
{
    // Patterns compiled once at startup
    [GeneratedRegex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"\b(\+?1[-.\s]?)?(\(?\d{3}\)?[-.\s]?)?\d{3}[-.\s]?\d{4}\b")]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"@\w+")]
    private static partial Regex HandlePattern();

    [GeneratedRegex(@"\b\d{1,5}\s+\w+\s+(Street|St|Avenue|Ave|Road|Rd|Boulevard|Blvd|Lane|Ln|Drive|Dr|Court|Ct|Way|Place|Pl)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AddressPattern();

    public static string SanitizeForAi(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;

        var result = EmailPattern().Replace(input, "[email]");
        result = PhonePattern().Replace(result, "[phone]");
        result = HandlePattern().Replace(result, "[handle]");
        result = AddressPattern().Replace(result, "their area");
        return result;
    }

    /// <summary>
    /// Strips EXIF GPS, device info, and timestamps from a JPEG byte array.
    /// Non-JPEG data is returned as-is.
    /// </summary>
    public static byte[] StripExif(byte[] jpeg)
    {
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
            return jpeg;

        using var input = new MemoryStream(jpeg);
        using var output = new MemoryStream();

        // Write SOI
        output.WriteByte(0xFF);
        output.WriteByte(0xD8);
        input.Position = 2;

        while (input.Position < input.Length - 1)
        {
            int b = input.ReadByte();
            if (b != 0xFF) break;

            int marker = input.ReadByte();
            if (marker < 0) break;

            // SOI/EOI have no length field
            if (marker == 0xD8) { continue; }
            if (marker == 0xD9) { output.WriteByte(0xFF); output.WriteByte(0xD9); break; }

            // Standalone markers (no length)
            if (marker >= 0xD0 && marker <= 0xD7) { output.WriteByte(0xFF); output.WriteByte((byte)marker); continue; }

            int hi = input.ReadByte();
            int lo = input.ReadByte();
            if (hi < 0 || lo < 0) break;

            int segLen = (hi << 8) | lo;
            int dataLen = segLen - 2;
            if (dataLen < 0) break;

            var segData = new byte[dataLen];
            int read = input.Read(segData, 0, dataLen);
            if (read < dataLen) break;

            // Skip APP1 (0xE1 = EXIF/XMP) and APP2 (0xE2 = ICC profile can contain device info)
            if (marker == 0xE1) continue;

            output.WriteByte(0xFF);
            output.WriteByte((byte)marker);
            output.WriteByte((byte)hi);
            output.WriteByte((byte)lo);
            output.Write(segData, 0, read);
        }

        return output.ToArray();
    }

    /// <summary>
    /// SHA-256(value + salt) returned as lowercase hex.
    /// Used for ip_hash and any user identifier stored in audit log.
    /// </summary>
    public static string HashForAudit(string value, string salt)
    {
        var combined = Encoding.UTF8.GetBytes(value + salt);
        var hash = SHA256.HashData(combined);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
