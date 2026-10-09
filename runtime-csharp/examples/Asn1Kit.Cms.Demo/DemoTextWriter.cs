using System.Globalization;
using System.Text;
using Asn1Kit.Runtime;

namespace Asn1Kit.Cms.Demo;

internal sealed class DemoTextWriter
{
    private readonly TextWriter _output;
    private int _indent;

    public DemoTextWriter(TextWriter output) => _output = output;

    public void Push() => _indent += 4;

    public void Pop() => _indent = Math.Max(0, _indent - 4);

    public void Line(string text)
    {
        _output.Write(new string(' ', _indent));
        _output.WriteLine(text);
    }

    public void Hex(ReadOnlySpan<byte> bytes, int maxPreview = 64)
    {
        if (bytes.IsEmpty)
        {
            Line("<empty>");
            return;
        }

        var show = bytes.Length <= maxPreview ? bytes : bytes[..maxPreview];
        var hex = Convert.ToHexString(show);
        var builder = new StringBuilder();
        for (var i = 0; i < hex.Length; i += 2)
        {
            if (i > 0)
                builder.Append(i % 32 == 0 ? Environment.NewLine + new string(' ', _indent) : " ");
            builder.Append(hex, i, 2);
        }

        Line(builder.ToString());
        if (bytes.Length > maxPreview)
            Line($"... ({bytes.Length} bytes total)");
    }

    public void BytesSummary(string label, ReadOnlySpan<byte> bytes)
    {
        Line($"{label}: {bytes.Length} bytes");
        Push();
        Hex(bytes);
        Pop();
    }

    public static string FormatTime(DateTimeOffset value) =>
        value.UtcDateTime.ToString("MMM dd HH:mm:ss yyyy", CultureInfo.InvariantCulture) + " GMT";

    public static string FormatSerial(Asn1Integer serial) => Convert.ToHexString(serial.Span);

    public static string FormatBitLength(Asn1BitString bits) =>
        bits.BitLength.ToString(CultureInfo.InvariantCulture) + " bit";
}
