using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Infrastructure.Authentication;

internal static class TotpUtility
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string GenerateSecret()
    {
        return Base32Encode(RandomNumberGenerator.GetBytes(20));
    }

    public static bool Validate(
        string secret,
        string code,
        DateTimeOffset now
    )
    {
        if (code.Length != 6
            || !code.All(char.IsAsciiDigit))
        {
            return false;
        }

        var counter = now.ToUnixTimeSeconds() / 30;
        for (var offset = -1; offset <= 1; offset++)
        {
            if (
                CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.ASCII.GetBytes(
                        GenerateCode(
                            secret,
                            counter + offset
                        )
                    ),
                    System.Text.Encoding.ASCII.GetBytes(code)
                )
            )
            {
                return true;
            }
        }

        return false;
    }

    private static string GenerateCode(
        string secret,
        long counter
    )
    {
        Span<byte> counterBytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(
            counterBytes,
            counter
        );
        using var hmac = new HMACSHA1(Base32Decode(secret));
        var hash = hmac.ComputeHash(counterBytes.ToArray());
        var offset = hash[^1] & 0x0F;
        var binary =
            ((hash[offset] & 0x7F) << 24)
            | ((hash[offset + 1] & 0xFF) << 16)
            | ((hash[offset + 2] & 0xFF) << 8)
            | (hash[offset + 3] & 0xFF);
        return (binary % 1_000_000).ToString(
            "D6",
            System.Globalization.CultureInfo.InvariantCulture
        );
    }

    private static string Base32Encode(
        byte[] data
    )
    {
        var output = new System.Text.StringBuilder((data.Length * 8 + 4) / 5);
        var buffer = 0;
        var bitsLeft = 0;
        foreach (var value in data)
        {
            buffer = (buffer << 8) | value;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                output.Append(Alphabet[(buffer >> (bitsLeft - 5)) & 31]);
                bitsLeft -= 5;
            }
        }
        if (bitsLeft > 0)
        {
            output.Append(Alphabet[(buffer << (5 - bitsLeft)) & 31]);
        }

        return output.ToString();
    }

    private static byte[] Base32Decode(
        string value
    )
    {
        var normalized = value
            .Trim()
            .TrimEnd('=')
            .ToUpperInvariant();
        var output = new List<byte>(normalized.Length * 5 / 8);
        var buffer = 0;
        var bitsLeft = 0;
        foreach (var character in normalized)
        {
            var index = Alphabet.IndexOf(character);
            if (index < 0)
            {
                throw new FormatException("Invalid Base32 secret.");
            }

            buffer = (buffer << 5) | index;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                output.Add((byte)(buffer >> (bitsLeft - 8)));
                bitsLeft -= 8;
            }
        }
        return output.ToArray();
    }
}
