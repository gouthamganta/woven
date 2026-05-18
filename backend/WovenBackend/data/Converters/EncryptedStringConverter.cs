using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using WovenBackend.Services.Security;

namespace WovenBackend.Data.Converters;

public class EncryptedStringConverter : ValueConverter<string?, string?>
{
    public EncryptedStringConverter(IEncryptionService enc)
        : base(
            v => v == null ? null : enc.Encrypt(v),
            v => v == null ? null : enc.Decrypt(v))
    { }
}
