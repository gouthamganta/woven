using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using WovenBackend.Services.Security;

namespace WovenBackend.Data.Converters;

public class EncryptedBytesConverter : ValueConverter<byte[], byte[]>
{
    public EncryptedBytesConverter(IEncryptionService enc)
        : base(
            v => enc.EncryptBytes(v),
            v => enc.DecryptBytes(v))
    { }
}
