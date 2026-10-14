using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace Infrastructure.Authentication;

public sealed class AesSecretProtector
{
    private readonly byte[] _key;

    public AesSecretProtector(
        IOptions<SecurityOptions> options
    )
    {
        try
        {
            _key = Convert.FromBase64String(options.Value.EncryptionKey);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "Security:EncryptionKey must be a base64-encoded 32-byte key.",
                exception
            );
        }

        if (_key.Length != 32)
        {
            throw new InvalidOperationException("Security:EncryptionKey must decode to exactly 32 bytes.");
        }
    }

    public string Protect(
        string value
    )
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintext = System.Text.Encoding.UTF8.GetBytes(value);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(
            _key,
            tag.Length
        );
        aes.Encrypt(
            nonce,
            plaintext,
            ciphertext,
            tag
        );
        return Convert.ToBase64String([1, .. nonce, .. tag, .. ciphertext]);
    }

    public string Unprotect(
        string protectedValue
    )
    {
        var payload = Convert.FromBase64String(protectedValue);
        if (payload.Length < 30
            || payload[0] != 1)
        {
            throw new CryptographicException("Unsupported protected secret format.");
        }

        var nonce = payload.AsSpan(
            1,
            12
        );
        var tag = payload.AsSpan(
            13,
            16
        );
        var ciphertext = payload.AsSpan(29);
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(
            _key,
            tag.Length
        );
        aes.Decrypt(
            nonce,
            ciphertext,
            tag,
            plaintext
        );
        return System.Text.Encoding.UTF8.GetString(plaintext);
    }
}
