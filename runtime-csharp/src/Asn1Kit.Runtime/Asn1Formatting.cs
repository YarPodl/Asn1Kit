using System.Globalization;

namespace Asn1Kit.Runtime;

/// <summary>Formats values for display without dumping encoded bytes.</summary>
public static class Asn1Formatting
{
    /// <summary>Formats a value independently of the current culture.</summary>
    public static string Format(object? value) => value switch
    {
        null => "null",
        ReadOnlyMemory<byte> bytes => ByteLength(bytes.Length),
        Memory<byte> bytes => ByteLength(bytes.Length),
        byte[] bytes => ByteLength(bytes.Length),
        DateTimeOffset time => time.ToString("O", CultureInfo.InvariantCulture),
        DateTime time => time.ToString("O", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "null",
        _ => value.ToString() ?? "null"
    };

    private static string ByteLength(int length) => length.ToString(CultureInfo.InvariantCulture) + " bytes";
}
