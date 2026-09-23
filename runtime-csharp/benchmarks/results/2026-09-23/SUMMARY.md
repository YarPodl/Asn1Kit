# Benchmark snapshot — 2026-09-23

| Field | Value |
| --- | --- |
| Date (local) | 2026-09-23 ~23:01 |
| Git | `7d281d1` (*Speed up Certificate.Decode and cut allocations on the hot path.*) |
| Working tree | clean (results-only untracked) |
| Command | `dotnet run -c Release --project runtime-csharp/benchmarks/Asn1Kit.Pkix.Benchmarks -- --filter *` (полный suite: Asn1Kit + BCL + BouncyCastle; peers отсюда — эталон для последующих `*Asn1Kit*`-прогонов на этой машине) |
| Host | Windows 11 (10.0.26200), .NET 6.0.10 / SDK 6.0.402, X64 RyuJIT AVX2 |
| Duration | ~8m 46s, 19 benchmarks |

Raw BDN exports in this folder: `*-report-github.md`, `*-report.csv`, `BenchmarkRun.log`.

## Certificate (`TrustAnchorRootCertificate.crt`)

| Method | Mean | Allocated |
| --- | ---: | ---: |
| Asn1Kit_Decode | 3.352 µs | 2912 B |
| Bcl_Decode | 2.624 µs | 328 B |
| BouncyCastle_Decode | 9.521 µs | 13280 B |
| Asn1Kit_Encode | 2.353 µs | 4232 B |
| BouncyCastle_Encode | 2.679 µs | 4792 B |

vs previous README “after Int32/Time + OID open-type” Cert Decode (3.436 µs / 2.84 KB): Mean ≈ same (−2%), Allocated unchanged.

## CertificateList / CRL (`GoodCACRL.crl`)

| Method | Mean | Allocated |
| --- | ---: | ---: |
| Asn1Kit_Decode | 2.634 µs | 2.31 KB |
| BouncyCastle_Decode | 7.325 µs | 10.45 KB |
| Asn1Kit_Encode | 2.089 µs | 2.82 KB |
| BouncyCastle_Encode | 2.169 µs | 4.01 KB |

## CMS ContentInfo (attached SignedData)

| Method | Mean | Allocated |
| --- | ---: | ---: |
| Asn1Kit_Lazy_Decode | 1.649 µs | 2.1 KB |
| Asn1Kit_Lazy_Materialize_Decode | 4.059 µs | 4.05 KB |
| Asn1Kit_Eager_Decode | 3.710 µs | 3.88 KB |
| Bcl_Decode | 1.599 µs | 3.45 KB |
| Bcl_Materialize_Decode | 8.676 µs | 4.68 KB |
| BouncyCastle_Decode | 7.538 µs | 13.59 KB |
| Asn1Kit_Lazy_Encode | 1.285 µs | 2.96 KB |
| Asn1Kit_Eager_Encode | 2.764 µs | 4.45 KB |
| Bcl_Encode | 1.967 µs | 8.85 KB |
| BouncyCastle_Encode | 2.647 µs | 5.11 KB |

vs previous README CMS snapshot (Lazy Decode 2.07 µs / 6.1 KB): Lazy Decode **≈20% faster**, Allocated **≈65% smaller**; Lazy Encode Mean 2.15 → 1.29 µs. Lazy Decode now within ~3% of BCL shell Decode.
