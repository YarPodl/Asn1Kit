using System.Buffers;
using Asn1Kit.Runtime;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace Asn1Kit.Benchmarks;

/// <summary>
/// Compares SEQUENCE OF fill strategies for unknown element count:
/// A = List + capacity heuristic (former), B = ArrayPool grow → exact T[],
/// C = pre-count TLV skip → exact T[]. Production ships B + single-element / Array.Empty fast paths.
/// </summary>
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class SequenceOfFillBenchmarks
{
    private byte[] _empty = null!;
    private byte[] _one = null!;
    private byte[] _many = null!;
    private byte[] _indefinite = null!;

    [GlobalSetup]
    public void Setup()
    {
        _empty = EncodeIntegers(Array.Empty<int>());
        _one = EncodeIntegers(new[] { 1 });
        _many = EncodeIntegers(Enumerable.Range(1, 16).ToArray());
        _indefinite = EncodeIntegersIndefinite(Enumerable.Range(1, 8).ToArray());
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Empty")]
    public int A_List_Empty() => DecodeList(_empty).Count;

    [Benchmark]
    [BenchmarkCategory("Empty")]
    public int B_Pool_Empty() => DecodePool(_empty).Length;

    [Benchmark]
    [BenchmarkCategory("Empty")]
    public int C_PreCount_Empty() => DecodePreCount(_empty).Length;

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("One")]
    public int A_List_One() => DecodeList(_one).Count;

    [Benchmark]
    [BenchmarkCategory("One")]
    public int B_Pool_One() => DecodePool(_one).Length;

    [Benchmark]
    [BenchmarkCategory("One")]
    public int C_PreCount_One() => DecodePreCount(_one).Length;

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Many16")]
    public int A_List_Many() => DecodeList(_many).Count;

    [Benchmark]
    [BenchmarkCategory("Many16")]
    public int B_Pool_Many() => DecodePool(_many).Length;

    [Benchmark]
    [BenchmarkCategory("Many16")]
    public int C_PreCount_Many() => DecodePreCount(_many).Length;

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Indefinite8")]
    public int A_List_Indefinite() => DecodeList(_indefinite, Asn1Encoding.Ber).Count;

    [Benchmark]
    [BenchmarkCategory("Indefinite8")]
    public int B_Pool_Indefinite() => DecodePool(_indefinite, Asn1Encoding.Ber).Length;

    [Benchmark]
    [BenchmarkCategory("Indefinite8")]
    public int C_PreCount_Indefinite() => DecodePreCount(_indefinite, Asn1Encoding.Ber).Length;

    private static byte[] EncodeIntegers(int[] values)
    {
        var writer = new Asn1Writer(Asn1Encoding.Der);
        writer.WriteSequenceOf(Asn1Tag.Sequence, values, static (w, v) => w.WriteInteger(Asn1Tag.Integer, v));
        return writer.Encode();
    }

    private static byte[] EncodeIntegersIndefinite(int[] values)
    {
        // 30 80 <INTEGERs...> 00 00
        using var ms = new MemoryStream();
        ms.WriteByte(0x30);
        ms.WriteByte(0x80);
        var writer = new Asn1Writer(Asn1Encoding.Der);
        foreach (var v in values)
        {
            writer.Reset();
            writer.WriteInteger(Asn1Tag.Integer, v);
            var item = writer.Encode();
            ms.Write(item, 0, item.Length);
        }

        ms.WriteByte(0x00);
        ms.WriteByte(0x00);
        return ms.ToArray();
    }

    /// <summary>A: List with capacity heuristic (former production).</summary>
    private static List<Asn1Integer> DecodeList(byte[] bytes, Asn1Encoding encoding = Asn1Encoding.Der)
    {
        var reader = new Asn1Reader(bytes, encoding);
        return reader.ReadSequence(Asn1Tag.Sequence, inner =>
        {
            var remaining = inner.Source.Length;
            if (remaining == 0)
            {
                return new List<Asn1Integer>();
            }

            var capacity = remaining <= 32 ? 1 : Math.Min(32, Math.Max(2, remaining / 16));
            var items = new List<Asn1Integer>(capacity);
            while (!inner.Eof)
            {
                items.Add(Asn1Integer.Decode(inner));
            }

            return items;
        });
    }

    /// <summary>B: ArrayPool grow, then exact T[].</summary>
    private static Asn1Integer[] DecodePool(byte[] bytes, Asn1Encoding encoding = Asn1Encoding.Der)
    {
        var reader = new Asn1Reader(bytes, encoding);
        return reader.ReadSequence(Asn1Tag.Sequence, inner =>
        {
            if (inner.Eof)
            {
                return Array.Empty<Asn1Integer>();
            }

            var rented = ArrayPool<Asn1Integer>.Shared.Rent(4);
            var count = 0;
            try
            {
                while (!inner.Eof)
                {
                    if (count == rented.Length)
                    {
                        var grown = ArrayPool<Asn1Integer>.Shared.Rent(rented.Length * 2);
                        Array.Copy(rented, grown, count);
                        ArrayPool<Asn1Integer>.Shared.Return(rented, clearArray: true);
                        rented = grown;
                    }

                    rented[count++] = Asn1Integer.Decode(inner);
                }

                if (count == 0)
                {
                    return Array.Empty<Asn1Integer>();
                }

                var result = new Asn1Integer[count];
                Array.Copy(rented, result, count);
                return result;
            }
            finally
            {
                ArrayPool<Asn1Integer>.Shared.Return(rented, clearArray: true);
            }
        });
    }

    /// <summary>C: pre-count top-level TLVs, then exact T[].</summary>
    private static Asn1Integer[] DecodePreCount(byte[] bytes, Asn1Encoding encoding = Asn1Encoding.Der)
    {
        var reader = new Asn1Reader(bytes, encoding);
        return reader.ReadSequence(Asn1Tag.Sequence, inner =>
        {
            // Snapshot contents window via Source (offset is at start inside ReadSequence).
            var contents = inner.Source;
            var counter = new Asn1Reader(contents, encoding, inner.Options);
            var count = 0;
            while (!counter.Eof)
            {
                _ = counter.ReadTlv();
                count++;
            }

            if (count == 0)
            {
                return Array.Empty<Asn1Integer>();
            }

            var items = new Asn1Integer[count];
            for (var i = 0; i < count; i++)
            {
                items[i] = Asn1Integer.Decode(inner);
            }

            return items;
        });
    }
}
