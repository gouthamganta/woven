namespace WovenBackend.Services.Security;

public interface IEncryptionService
{
    string Encrypt(string plaintext);
    string Decrypt(string ciphertext);
    byte[] EncryptBytes(byte[] data);
    byte[] DecryptBytes(byte[] ciphertext);

    /// <summary>
    /// Derives a purpose-scoped key via HKDF from the master key.
    /// Valid purposes: "column-encryption-v1", "cache-encryption-v1", "signing-v1"
    /// </summary>
    string DeriveKey(string purpose);

    /// <summary>
    /// Computes deterministic HMAC-SHA256 hash of email (lowercased) for searchable identity.
    /// Used to find existing accounts without decrypting all encrypted emails.
    /// </summary>
    string ComputeEmailHash(string email);
}
