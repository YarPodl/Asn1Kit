using Asn1Kit.Runtime;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace Asn1Kit.Benchmarks;

/// <summary>
/// Isolates closure cost for SEQUENCE OF fill vs production ReadSequenceOf (EnterSequence, no capture).
/// Nest path is EnterSequence-only (ReadSequence(Func) removed).
/// </summary>
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class SequenceLambdaBenchmarks
{
    private byte[] _nested = null!;
    private byte[] _ofMany = null!;
    private byte[] _ofOne = null!;

    [GlobalSetup]
    public void Setup()
    {
        var nestedWriter = new Asn1Writer(Asn1Encoding.Der);
        nestedWriter.WriteSequence(Asn1Tag.Sequence, outer =>
        {
            outer.WriteInteger(Asn1Tag.Integer, 1);
            outer.WriteSequence(Asn1Tag.Sequence, mid =>
            {
                mid.WriteInteger(Asn1Tag.Integer, 2);
                mid.WriteSequence(Asn1Tag.Sequence, inner => inner.WriteInteger(Asn1Tag.Integer, 3));
            });
        });
        _nested = nestedWriter.Encode();

        var many = new Asn1Integer[16];
        for (var i = 0; i < many.Length; i++)
        {
            many[i] = Asn1Integer.FromInt32(i);
        }

        var ofManyWriter = new Asn1Writer(Asn1Encoding.Der);
        ofManyWriter.WriteSequenceOf(Asn1Tag.Sequence, many, static (w, item) => Asn1Integer.Encode(w, item));
        _ofMany = ofManyWriter.Encode();

        var ofOneWriter = new Asn1Writer(Asn1Encoding.Der);
        ofOneWriter.WriteSequenceOf(Asn1Tag.Sequence, new[] { Asn1Integer.FromInt32(7) }, static (w, item) => Asn1Integer.Encode(w, item));
        _ofOne = ofOneWriter.Encode();
    }

    [Benchmark]
    [BenchmarkCategory("Nest")]
    public int Cursor_NestedSequence()
    {
        var reader = new Asn1Reader(_nested, Asn1Encoding.Der);
        int a;
        int b;
        int c;
        using (reader.EnterSequence(Asn1Tag.Sequence))
        {
            a = reader.ReadInt32(Asn1Tag.Integer);
            using (reader.EnterSequence(Asn1Tag.Sequence))
            {
                b = reader.ReadInt32(Asn1Tag.Integer);
                using (reader.EnterSequence(Asn1Tag.Sequence))
                {
                    c = reader.ReadInt32(Asn1Tag.Integer);
                }
            }
        }

        return a + b + c;
    }

    /// <summary>Legacy pattern: capturing Func body closes over decodeItem (allocates every call).</summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("OfMany")]
    public int A_OfCapturing_Many16()
    {
        var reader = new Asn1Reader(_ofMany, Asn1Encoding.Der);
        var items = ReadSequenceOfCapturing(reader, Asn1Tag.Sequence, static inner => Asn1Integer.Decode(inner));
        return items.Length + items[0].GetInt32();
    }

    [Benchmark]
    [BenchmarkCategory("OfMany")]
    public int C_OfNoClosure_Many16()
    {
        var reader = new Asn1Reader(_ofMany, Asn1Encoding.Der);
        var items = reader.ReadSequenceOf(Asn1Tag.Sequence, static inner => Asn1Integer.Decode(inner));
        return items.Length + items[0].GetInt32();
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("OfOne")]
    public int A_OfCapturing_One()
    {
        var reader = new Asn1Reader(_ofOne, Asn1Encoding.Der);
        var items = ReadSequenceOfCapturing(reader, Asn1Tag.Sequence, static inner => Asn1Integer.Decode(inner));
        return items.Length + items[0].GetInt32();
    }

    [Benchmark]
    [BenchmarkCategory("OfOne")]
    public int C_OfNoClosure_One()
    {
        var reader = new Asn1Reader(_ofOne, Asn1Encoding.Der);
        var items = reader.ReadSequenceOf(Asn1Tag.Sequence, static inner => Asn1Integer.Decode(inner));
        return items.Length + items[0].GetInt32();
    }

    /// <summary>Pre-fix ReadSequenceOf shape: always allocates a closure capturing <paramref name="decodeItem"/>.</summary>
    private static T[] ReadSequenceOfCapturing<T>(Asn1Reader reader, Asn1Tag expected, Func<Asn1Reader, T> decodeItem)
    {
        using var cursor = reader.EnterSequence(expected);
        Func<Asn1Reader, T[]> fill = inner =>
        {
            if (inner.Eof)
            {
                return Array.Empty<T>();
            }

            var first = decodeItem(inner);
            if (inner.Eof)
            {
                return new[] { first };
            }

            var rented = System.Buffers.ArrayPool<T>.Shared.Rent(8);
            var count = 0;
            try
            {
                rented[count++] = first;
                while (!inner.Eof)
                {
                    if (count == rented.Length)
                    {
                        var grown = System.Buffers.ArrayPool<T>.Shared.Rent(rented.Length * 2);
                        Array.Copy(rented, grown, count);
                        System.Buffers.ArrayPool<T>.Shared.Return(rented, clearArray: true);
                        rented = grown;
                    }

                    rented[count++] = decodeItem(inner);
                }

                var result = new T[count];
                Array.Copy(rented, result, count);
                return result;
            }
            finally
            {
                System.Buffers.ArrayPool<T>.Shared.Return(rented, clearArray: true);
            }
        };

        return fill(reader);
    }
}
