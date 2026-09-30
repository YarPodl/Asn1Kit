using System.Numerics;
using Asn1Kit.Runtime;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace Asn1Kit.Benchmarks;

/// <summary>Measures constructed-length finalization and numeric Asn1Integer factories.</summary>
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class WriterOptimizationBenchmarks
{
    private Asn1Writer _writer = null!;
    private Action<Asn1Writer> _write128 = null!;
    private Action<Asn1Writer> _write256 = null!;
    private Action<Asn1Writer> _write64K = null!;

    [GlobalSetup]
    public void Setup()
    {
        _writer = new Asn1Writer(Asn1Encoding.Der);
        _writer.EnsureCapacity(70_000);
        _write128 = CreateRawWriter(128);
        _write256 = CreateRawWriter(256);
        _write64K = CreateRawWriter(65_536);
    }

    [Benchmark]
    [BenchmarkCategory("Constructed", "Short")]
    public int ShortSiblingSequences()
    {
        _writer.Reset();
        for (var i = 0; i < 32; i++)
        {
            _writer.WriteSequence(Asn1Tag.Sequence, static inner => inner.WriteNull(Asn1Tag.Null));
        }

        return _writer.EncodedLength;
    }

    [Benchmark]
    [BenchmarkCategory("Constructed", "Short")]
    public int DeepShortSequence()
    {
        _writer.Reset();
        _writer.WriteSequence(Asn1Tag.Sequence, static level1 =>
            level1.WriteSequence(Asn1Tag.Sequence, static level2 =>
                level2.WriteSequence(Asn1Tag.Sequence, static level3 =>
                    level3.WriteSequence(Asn1Tag.Sequence, static level4 =>
                        level4.WriteSequence(Asn1Tag.Sequence, static level5 =>
                            level5.WriteSequence(Asn1Tag.Sequence, static level6 =>
                                level6.WriteSequence(Asn1Tag.Sequence, static level7 =>
                                    level7.WriteSequence(Asn1Tag.Sequence, static level8 =>
                                        level8.WriteInteger(Asn1Tag.Integer, 1)))))))));

        return _writer.EncodedLength;
    }

    [Benchmark]
    [BenchmarkCategory("Constructed", "Long")]
    public int Sequence128() => WriteLongSequence(_write128);

    [Benchmark]
    [BenchmarkCategory("Constructed", "Long")]
    public int Sequence256() => WriteLongSequence(_write256);

    [Benchmark]
    [BenchmarkCategory("Constructed", "Long")]
    public int Sequence64K() => WriteLongSequence(_write64K);

    [Benchmark]
    [BenchmarkCategory("Integer", "Cached")]
    public int CachedIntegerFactories()
    {
        var result = 0;
        for (var i = -128; i <= 127; i++)
        {
            result += Asn1Integer.FromInt32(i).GetInt32();
            result += Asn1Integer.FromInt64(i).GetInt32();
            if (i >= 0)
            {
                result += Asn1Integer.FromUInt32((uint)i).GetInt32();
                result += Asn1Integer.FromUInt64((ulong)i).GetInt32();
            }

            result += Asn1Integer.FromBigInteger(new BigInteger(i)).GetInt32();
        }

        return result;
    }

    [Benchmark]
    [BenchmarkCategory("Integer", "Uncached")]
    public int UncachedIntegerFactories()
    {
        var result = 0;
        for (var i = 128; i < 384; i++)
        {
            result += Asn1Integer.FromInt32(i).GetInt32();
            result += Asn1Integer.FromInt64(i).GetInt32();
            result += Asn1Integer.FromUInt32((uint)i).GetInt32();
            result += Asn1Integer.FromUInt64((ulong)i).GetInt32();
            result += Asn1Integer.FromBigInteger(new BigInteger(i)).GetInt32();
        }

        return result;
    }

    private int WriteLongSequence(Action<Asn1Writer> writeContents)
    {
        _writer.Reset();
        _writer.WriteSequence(Asn1Tag.Sequence, writeContents);
        return _writer.EncodedLength;
    }

    private static Action<Asn1Writer> CreateRawWriter(int length)
    {
        var contents = new byte[length];
        return writer => writer.WriteRaw(contents);
    }
}
