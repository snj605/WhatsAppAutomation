using System.Text;

namespace WhatsAppAutomation.Domain.ValueObjects;

/// <summary>
/// A phone number in international E.164 form, for example "+919876543210": a plus, the country
/// code and the number, at most 15 digits. WhatsApp identifies people by this number.
/// </summary>
public static class PhoneNumber
{
    /// <summary>Longest stored form: "+" and 15 digits.</summary>
    public const int MaxLength = 16;

    /// <summary>
    /// Accepts what people type: spaces, dashes, dots and brackets are removed, and "00" at the
    /// start counts as "+". The country code is required: there is no default country (yet).
    /// Returns null when the result is not a valid E.164 number.
    /// </summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var text = input.Trim();
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsAsciiDigit(c) || (c == '+' && builder.Length == 0))
            {
                builder.Append(c);
            }
            else if (c is not (' ' or '-' or '.' or '(' or ')'))
            {
                return null;
            }
        }

        var cleaned = builder.ToString();
        if (cleaned.StartsWith("00", StringComparison.Ordinal))
        {
            cleaned = "+" + cleaned[2..];
        }

        if (!cleaned.StartsWith('+'))
        {
            return null;
        }

        var digits = cleaned[1..];
        return digits.Length is >= 8 and <= 15 && digits[0] != '0' && digits.All(char.IsAsciiDigit) ? cleaned : null;
    }
}
