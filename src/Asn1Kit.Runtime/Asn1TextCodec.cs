using System.Globalization;
using System.Text;

namespace Asn1Kit.Runtime;

/// <summary>Shared encode/decode helpers for character strings and time values.</summary>
internal static class Asn1TextCodec
{
    private static readonly Encoding Utf8Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly Encoding Latin1 = Encoding.Latin1;

    public static Asn1Tag DefaultStringTag(Asn1StringForm form) => form switch
    {
        Asn1StringForm.Utf8 => Asn1Tag.Utf8String,
        Asn1StringForm.Numeric => Asn1Tag.NumericString,
        Asn1StringForm.Printable => Asn1Tag.PrintableString,
        Asn1StringForm.Teletex or Asn1StringForm.T61 => Asn1Tag.TeletexString,
        Asn1StringForm.Videotex => Asn1Tag.VideotexString,
        Asn1StringForm.Ia5 => Asn1Tag.Ia5String,
        Asn1StringForm.Graphic => Asn1Tag.GraphicString,
        Asn1StringForm.Visible => Asn1Tag.VisibleString,
        Asn1StringForm.General => Asn1Tag.GeneralString,
        Asn1StringForm.Universal => Asn1Tag.UniversalString,
        Asn1StringForm.Bmp => Asn1Tag.BmpString,
        _ => throw new Asn1Exception($"Unknown string form '{form}'.")
    };

    public static Asn1Tag DefaultTimeTag(Asn1TimeForm form) => form switch
    {
        Asn1TimeForm.Utc => Asn1Tag.UtcTime,
        Asn1TimeForm.Generalized => Asn1Tag.GeneralizedTime,
        _ => throw new Asn1Exception($"Unknown time form '{form}'.")
    };

    public static byte[] EncodeString(string value, Asn1StringForm form)
    {
        if (value is null)
        {
            throw new Asn1Exception("String value must not be null.");
        }

        return form switch
        {
            Asn1StringForm.Utf8 => EncodeUtf8(value),
            Asn1StringForm.Bmp => EncodeBmp(value),
            Asn1StringForm.Universal => EncodeUniversal(value),
            Asn1StringForm.Numeric => EncodeAsciiChecked(value, IsNumeric),
            Asn1StringForm.Printable => EncodeAsciiChecked(value, IsPrintable),
            Asn1StringForm.Ia5 => EncodeAsciiChecked(value, IsIa5),
            Asn1StringForm.Visible => EncodeAsciiChecked(value, IsVisible),
            Asn1StringForm.Teletex or Asn1StringForm.T61 or Asn1StringForm.Videotex
                or Asn1StringForm.Graphic or Asn1StringForm.General => Latin1.GetBytes(value),
            _ => throw new Asn1Exception($"Unknown string form '{form}'.")
        };
    }

    public static string DecodeString(ReadOnlySpan<byte> contents, Asn1StringForm form)
    {
        return form switch
        {
            Asn1StringForm.Utf8 => DecodeUtf8(contents),
            Asn1StringForm.Bmp => DecodeBmp(contents),
            Asn1StringForm.Universal => DecodeUniversal(contents),
            Asn1StringForm.Numeric => DecodeAsciiChecked(contents, IsNumeric, "NumericString"),
            Asn1StringForm.Printable => DecodeAsciiChecked(contents, IsPrintable, "PrintableString"),
            Asn1StringForm.Ia5 => DecodeAsciiChecked(contents, IsIa5, "IA5String"),
            Asn1StringForm.Visible => DecodeAsciiChecked(contents, IsVisible, "VisibleString"),
            Asn1StringForm.Teletex or Asn1StringForm.T61 or Asn1StringForm.Videotex
                or Asn1StringForm.Graphic or Asn1StringForm.General => Latin1.GetString(contents),
            _ => throw new Asn1Exception($"Unknown string form '{form}'.")
        };
    }

    public static string FormatTime(DateTimeOffset value, Asn1TimeForm form)
    {
        var utc = value.ToUniversalTime();
        return form switch
        {
            Asn1TimeForm.Utc => FormatUtc(utc),
            Asn1TimeForm.Generalized => FormatGeneralized(utc),
            _ => throw new Asn1Exception($"Unknown time form '{form}'.")
        };
    }

    public static DateTimeOffset ParseTime(string text, Asn1TimeForm form, Asn1Encoding encoding)
    {
        if (string.IsNullOrEmpty(text))
        {
            throw new Asn1Exception("Time value is empty.");
        }

        return form switch
        {
            Asn1TimeForm.Utc => ParseUtc(text, encoding),
            Asn1TimeForm.Generalized => ParseGeneralized(text, encoding),
            _ => throw new Asn1Exception($"Unknown time form '{form}'.")
        };
    }

    public static void EnsureTrailingBitsZero(ReadOnlySpan<byte> bytes, int unusedBits)
    {
        if (unusedBits == 0 || bytes.Length == 0)
        {
            return;
        }

        var mask = (byte)((1 << unusedBits) - 1);
        if ((bytes[^1] & mask) != 0)
        {
            throw new Asn1Exception("DER BIT STRING trailing bits must be zero.");
        }
    }

    private static byte[] EncodeUtf8(string value)
    {
        try
        {
            return Utf8Strict.GetBytes(value);
        }
        catch (EncoderFallbackException ex)
        {
            throw new Asn1Exception($"Invalid UTF-8 string: {ex.Message}");
        }
    }

    private static string DecodeUtf8(ReadOnlySpan<byte> contents)
    {
        try
        {
            return Utf8Strict.GetString(contents);
        }
        catch (DecoderFallbackException ex)
        {
            throw new Asn1Exception($"Invalid UTF-8 contents: {ex.Message}");
        }
    }

    private static byte[] EncodeBmp(string value)
    {
        var bytes = new byte[value.Length * 2];
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            bytes[i * 2] = (byte)(c >> 8);
            bytes[i * 2 + 1] = (byte)c;
        }

        return bytes;
    }

    private static string DecodeBmp(ReadOnlySpan<byte> contents)
    {
        if ((contents.Length & 1) != 0)
        {
            throw new Asn1Exception("BMPString contents length must be even.");
        }

        var chars = new char[contents.Length / 2];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = (char)((contents[i * 2] << 8) | contents[i * 2 + 1]);
        }

        return new string(chars);
    }

    private static byte[] EncodeUniversal(string value)
    {
        var codePoints = new List<int>();
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]))
            {
                if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1]))
                {
                    throw new Asn1Exception("UniversalString contains an incomplete surrogate pair.");
                }

                codePoints.Add(char.ConvertToUtf32(value[i], value[i + 1]));
                i++;
            }
            else if (char.IsLowSurrogate(value[i]))
            {
                throw new Asn1Exception("UniversalString contains an unpaired low surrogate.");
            }
            else
            {
                codePoints.Add(value[i]);
            }
        }

        var bytes = new byte[codePoints.Count * 4];
        for (var i = 0; i < codePoints.Count; i++)
        {
            var cp = codePoints[i];
            bytes[i * 4] = (byte)(cp >> 24);
            bytes[i * 4 + 1] = (byte)(cp >> 16);
            bytes[i * 4 + 2] = (byte)(cp >> 8);
            bytes[i * 4 + 3] = (byte)cp;
        }

        return bytes;
    }

    private static string DecodeUniversal(ReadOnlySpan<byte> contents)
    {
        if ((contents.Length & 3) != 0)
        {
            throw new Asn1Exception("UniversalString contents length must be a multiple of 4.");
        }

        var builder = new StringBuilder(contents.Length / 4);
        for (var i = 0; i < contents.Length; i += 4)
        {
            var cp = (contents[i] << 24) | (contents[i + 1] << 16) | (contents[i + 2] << 8) | contents[i + 3];
            if (cp is < 0 or > 0x10FFFF || (cp is >= 0xD800 and <= 0xDFFF))
            {
                throw new Asn1Exception("UniversalString contains an invalid code point.");
            }

            builder.Append(char.ConvertFromUtf32(cp));
        }

        return builder.ToString();
    }

    private static byte[] EncodeAsciiChecked(string value, Func<char, bool> allowed)
    {
        var bytes = new byte[value.Length];
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (!allowed(c))
            {
                throw new Asn1Exception($"Character U+{((int)c).ToString("X4", CultureInfo.InvariantCulture)} is not allowed.");
            }

            bytes[i] = (byte)c;
        }

        return bytes;
    }

    private static string DecodeAsciiChecked(ReadOnlySpan<byte> contents, Func<char, bool> allowed, string name)
    {
        var chars = new char[contents.Length];
        for (var i = 0; i < contents.Length; i++)
        {
            var c = (char)contents[i];
            if (!allowed(c))
            {
                throw new Asn1Exception($"{name} contains an invalid character at offset {i}.");
            }

            chars[i] = c;
        }

        return new string(chars);
    }

    private static bool IsNumeric(char c) => c is >= '0' and <= '9' or ' ';

    private static bool IsPrintable(char c) =>
        c is >= 'A' and <= 'Z'
            or >= 'a' and <= 'z'
            or >= '0' and <= '9'
            or ' ' or '\'' or '(' or ')' or '+' or ',' or '-' or '.' or '/' or ':' or '=' or '?';

    private static bool IsIa5(char c) => c <= 0x7F;

    private static bool IsVisible(char c) => c is >= (char)0x20 and <= (char)0x7E;

    private static string FormatUtc(DateTimeOffset utc) =>
        string.Create(13, utc, (span, value) =>
        {
            Write2(span, 0, value.Year % 100);
            Write2(span, 2, value.Month);
            Write2(span, 4, value.Day);
            Write2(span, 6, value.Hour);
            Write2(span, 8, value.Minute);
            Write2(span, 10, value.Second);
            span[12] = 'Z';
        });

    private static string FormatGeneralized(DateTimeOffset utc)
    {
        var fractionTicks = (int)(utc.Ticks % TimeSpan.TicksPerSecond);
        if (fractionTicks == 0)
        {
            return string.Create(15, utc, (span, value) =>
            {
                Write4(span, 0, value.Year);
                Write2(span, 4, value.Month);
                Write2(span, 6, value.Day);
                Write2(span, 8, value.Hour);
                Write2(span, 10, value.Minute);
                Write2(span, 12, value.Second);
                span[14] = 'Z';
            });
        }

        // DER (X.690 §11.7): seconds + optional fraction without trailing zeros, terminate with Z.
        // Precision follows DateTimeOffset (100 ns → at most 7 digits after TrimEnd('0')).
        var frac = fractionTicks.ToString("D7", CultureInfo.InvariantCulture).TrimEnd('0');
        return string.Create(15 + 1 + frac.Length, (utc, frac), (span, state) =>
        {
            Write4(span, 0, state.utc.Year);
            Write2(span, 4, state.utc.Month);
            Write2(span, 6, state.utc.Day);
            Write2(span, 8, state.utc.Hour);
            Write2(span, 10, state.utc.Minute);
            Write2(span, 12, state.utc.Second);
            span[14] = '.';
            state.frac.AsSpan().CopyTo(span[15..]);
            span[^1] = 'Z';
        });
    }

    private static DateTimeOffset ParseUtc(string text, Asn1Encoding encoding)
    {
        // YYMMDDHHMM[SS][Z|+hhmm|-hhmm]
        if (text.Length < 10)
        {
            throw new Asn1Exception("UTCTime is too short.");
        }

        var yy = Read2(text, 0);
        var month = Read2(text, 2);
        var day = Read2(text, 4);
        var hour = Read2(text, 6);
        var minute = Read2(text, 8);
        var year = yy <= 49 ? 2000 + yy : 1900 + yy;

        var index = 10;
        int second;
        if (index + 1 < text.Length && char.IsDigit(text[index]) && char.IsDigit(text[index + 1]))
        {
            second = Read2(text, index);
            index += 2;
        }
        else
        {
            if (encoding == Asn1Encoding.Der)
            {
                throw new Asn1Exception("DER UTCTime must include seconds.");
            }

            second = 0;
        }

        var offset = ParseZone(text, index, encoding, requireZ: encoding == Asn1Encoding.Der);
        return CreateDateTime(year, month, day, hour, minute, second, 0, offset);
    }

    private static DateTimeOffset ParseGeneralized(string text, Asn1Encoding encoding)
    {
        // YYYYMMDDHHMM[SS][.f–fffffff][Z|+hhmm|-hhmm] — fraction length on input is 1..7 digits.
        if (text.Length < 12)
        {
            throw new Asn1Exception("GeneralizedTime is too short.");
        }

        var year = Read4(text, 0);
        var month = Read2(text, 4);
        var day = Read2(text, 6);
        var hour = Read2(text, 8);
        var minute = Read2(text, 10);
        var index = 12;
        int second;
        if (index + 1 < text.Length && char.IsDigit(text[index]) && char.IsDigit(text[index + 1]))
        {
            second = Read2(text, index);
            index += 2;
        }
        else
        {
            if (encoding == Asn1Encoding.Der)
            {
                throw new Asn1Exception("DER GeneralizedTime must include seconds.");
            }

            second = 0;
        }

        var fractionTicks = 0;
        if (index < text.Length && (text[index] == '.' || text[index] == ','))
        {
            var separator = text[index];
            if (encoding == Asn1Encoding.Der && separator == ',')
            {
                throw new Asn1Exception("DER GeneralizedTime must use '.' as the fraction separator.");
            }

            index++;
            var start = index;
            while (index < text.Length && char.IsDigit(text[index]))
            {
                index++;
            }

            var digitCount = index - start;
            if (digitCount is < 1 or > 7)
            {
                throw new Asn1Exception("GeneralizedTime fraction must have 1 to 7 digits.");
            }

            var frac = text[start..index];
            if (encoding == Asn1Encoding.Der && frac.EndsWith('0'))
            {
                throw new Asn1Exception("DER GeneralizedTime fraction must not have trailing zeros.");
            }

            fractionTicks = int.Parse(frac.PadRight(7, '0'), CultureInfo.InvariantCulture);
        }

        var offset = ParseZone(text, index, encoding, requireZ: encoding == Asn1Encoding.Der);
        return CreateDateTime(year, month, day, hour, minute, second, fractionTicks, offset);
    }

    private static TimeSpan ParseZone(string text, int index, Asn1Encoding encoding, bool requireZ)
    {
        if (index >= text.Length)
        {
            throw new Asn1Exception("Time value is missing a time zone.");
        }

        if (text[index] == 'Z')
        {
            if (index + 1 != text.Length)
            {
                throw new Asn1Exception("Unexpected characters after 'Z' in time value.");
            }

            return TimeSpan.Zero;
        }

        if (requireZ || encoding == Asn1Encoding.Der)
        {
            throw new Asn1Exception("DER time values must end with 'Z'.");
        }

        if (text[index] is not ('+' or '-'))
        {
            throw new Asn1Exception("Invalid time zone in time value.");
        }

        var sign = text[index] == '+' ? 1 : -1;
        if (index + 5 != text.Length)
        {
            throw new Asn1Exception("Time zone offset must be +hhmm or -hhmm.");
        }

        var hh = Read2(text, index + 1);
        var mm = Read2(text, index + 3);
        if (hh > 23 || mm > 59)
        {
            throw new Asn1Exception("Time zone offset is out of range.");
        }

        return TimeSpan.FromMinutes(sign * (hh * 60 + mm));
    }

    private static DateTimeOffset CreateDateTime(
        int year, int month, int day, int hour, int minute, int second, int fractionTicks, TimeSpan offset)
    {
        if (fractionTicks is < 0 or >= (int)TimeSpan.TicksPerSecond)
        {
            throw new Asn1Exception("Fractional second is out of range.");
        }

        try
        {
            var local = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified)
                .AddTicks(fractionTicks);
            return new DateTimeOffset(local, offset).ToUniversalTime();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new Asn1Exception($"Invalid date/time components: {ex.Message}");
        }
    }

    private static int Read2(string text, int index)
    {
        if (index + 1 >= text.Length || !char.IsDigit(text[index]) || !char.IsDigit(text[index + 1]))
        {
            throw new Asn1Exception("Expected two decimal digits in time value.");
        }

        return (text[index] - '0') * 10 + (text[index + 1] - '0');
    }

    private static int Read4(string text, int index)
    {
        if (index + 3 >= text.Length
            || !char.IsDigit(text[index])
            || !char.IsDigit(text[index + 1])
            || !char.IsDigit(text[index + 2])
            || !char.IsDigit(text[index + 3]))
        {
            throw new Asn1Exception("Expected four decimal digits in time value.");
        }

        return (text[index] - '0') * 1000
            + (text[index + 1] - '0') * 100
            + (text[index + 2] - '0') * 10
            + (text[index + 3] - '0');
    }

    private static void Write2(Span<char> span, int offset, int value)
    {
        span[offset] = (char)('0' + value / 10);
        span[offset + 1] = (char)('0' + value % 10);
    }

    private static void Write4(Span<char> span, int offset, int value)
    {
        span[offset] = (char)('0' + value / 1000);
        span[offset + 1] = (char)('0' + (value / 100) % 10);
        span[offset + 2] = (char)('0' + (value / 10) % 10);
        span[offset + 3] = (char)('0' + value % 10);
    }
}
