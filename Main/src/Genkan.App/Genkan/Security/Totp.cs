using System.Security.Cryptography;
using System.Text;

namespace Genkan.Security;

/// <summary>极简 TOTP（RFC 6238），不依赖第三方库。</summary>
public static class Totp
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string GenerateSecret(int byteLength = 20)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteLength);
        return Base32Encode(bytes);
    }

    public static string Base32Encode(ReadOnlySpan<byte> data)
    {
        var builder = new StringBuilder();
        var buffer = 0;
        var bits = 0;

        foreach (var value in data)
        {
            buffer = (buffer << 8) | value;
            bits += 8;

            while (bits >= 5)
            {
                builder.Append(Alphabet[(buffer >> (bits - 5)) & 0x1F]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            builder.Append(Alphabet[(buffer << (5 - bits)) & 0x1F]);
        }

        return builder.ToString();
    }

    public static byte[] Base32Decode(string input)
    {
        var cleaned = input.Trim().Replace(" ", string.Empty).TrimEnd('=').ToUpperInvariant();
        var bytes = new List<byte>(cleaned.Length * 5 / 8);
        var buffer = 0;
        var bits = 0;

        foreach (var character in cleaned)
        {
            var index = Alphabet.IndexOf(character);
            if (index < 0)
            {
                throw new FormatException("不是合法的 Base32 字符串。");
            }

            buffer = (buffer << 5) | index;
            bits += 5;

            if (bits >= 8)
            {
                bytes.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return bytes.ToArray();
    }

    public static string Compute(string secret, long unixSeconds, int digits = 6, int step = 30)
    {
        var counter = unixSeconds / step;
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(counterBytes);
        }

        var key = Base32Decode(secret);
        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(counterBytes);

        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
                     | ((hash[offset + 1] & 0xFF) << 16)
                     | ((hash[offset + 2] & 0xFF) << 8)
                     | (hash[offset + 3] & 0xFF);

        var code = binary % (int)Math.Pow(10, digits);
        return code.ToString().PadLeft(digits, '0');
    }

    public static bool Validate(string secret, string? code, int window = 1)
    {
        var trimmed = (code ?? string.Empty).Replace(" ", string.Empty);
        if (trimmed.Length != 6 || !trimmed.All(char.IsDigit))
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        for (var offset = -window; offset <= window; offset++)
        {
            var candidate = Compute(secret, now + offset * 30);
            if (CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(candidate),
                    Encoding.ASCII.GetBytes(trimmed)))
            {
                return true;
            }
        }

        return false;
    }

    public static string BuildUri(string secret, string account, string issuer)
        => $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}" +
           $"?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits=6&period=30";
}
