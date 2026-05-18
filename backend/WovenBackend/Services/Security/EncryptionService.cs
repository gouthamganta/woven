using System.Security.Cryptography;
using System.Text;

namespace WovenBackend.Services.Security;

public class EncryptionService : IEncryptionService
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _masterKey;

    public EncryptionService(IConfiguration config)
    {
        var keyB64 = config["Encryption:MasterKey"]
            ?? throw new InvalidOperationException("Encryption:MasterKey is required");
        _masterKey = Convert.FromBase64String(keyB64);
        if (_masterKey.Length != 32)
            throw new InvalidOperationException("Encryption:MasterKey must be exactly 32 bytes (base64-encoded)");
    }

    public string Encrypt(string plaintext)
    {
        var encrypted = EncryptBytes(Encoding.UTF8.GetBytes(plaintext));
        return Convert.ToBase64String(encrypted);
    }

    public string Decrypt(string ciphertext)
    {
        var decrypted = DecryptBytes(Convert.FromBase64String(ciphertext));
        return Encoding.UTF8.GetString(decrypted);
    }

    public byte[] EncryptBytes(byte[] data)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var ciphertext = new byte[data.Length];

        using var aes = new AesGcm(_masterKey, TagSize);
        aes.Encrypt(nonce, data, ciphertext, tag);

        // Output: nonce[12] + ciphertext + tag[16]
        var result = new byte[NonceSize + ciphertext.Length + TagSize];
        nonce.CopyTo(result, 0);
        ciphertext.CopyTo(result, NonceSize);
        tag.CopyTo(result, NonceSize + ciphertext.Length);
        return result;
    }

    public byte[] DecryptBytes(byte[] combined)
    {
        if (combined.Length < NonceSize + TagSize)
            throw new CryptographicException("Ciphertext too short");

        var nonce = combined[..NonceSize];
        var tag = combined[^TagSize..];
        var ciphertext = combined[NonceSize..^TagSize];
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(_masterKey, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }

    public string DeriveKey(string purpose)
    {
        // HKDF-SHA256: master key as IKM, purpose as info, no salt
        var derived = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            _masterKey,
            outputLength: 32,
            salt: [],
            info: Encoding.UTF8.GetBytes(purpose));
        return Convert.ToBase64String(derived);
    }
}
