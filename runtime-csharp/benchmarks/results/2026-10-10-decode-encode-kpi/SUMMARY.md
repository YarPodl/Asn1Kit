# Benchmark snapshot — 2026-10-10 (Decode+Encode KPI after Binding unify)

| Field | Value |
| --- | --- |
| Date (local) | 2026-10-10 |
| Git | `8dc394b` (+ bench: Binding/`Asn1Any`; CMS Lazy = ContentInfo + `TryDecodeContent(SignedData)`) |
| Host | Windows 11 (10.0.26200), .NET 6.0.10 / SDK 6.0.402, X64 RyuJIT AVX2 |
| Command | `dotnet run -c Release --project runtime-csharp/benchmarks/Asn1Kit.Pkix.Benchmarks -- --filter "*CertificateBenchmarks.Asn1Kit_Decode" "*CertificateBenchmarks.Asn1Kit_Encode" "*CertificateListBenchmarks.Asn1Kit_Decode" "*CertificateListBenchmarks.Asn1Kit_Encode" "*CmsBenchmarks.Asn1Kit_Lazy_Decode" "*CmsBenchmarks.Asn1Kit_Lazy_Encode"` |
| Alloc profile | `--alloc-profile` → [alloc-profile.txt](alloc-profile.txt) |

Compare Decode to [2026-10-09-decode-kpi](../2026-10-09-decode-kpi/) (eager typed open-type) and [2026-09-24-explicit-enter](../2026-09-24-explicit-enter/) (opaque `Asn1Any` era). Encode vs [2026-09-30-writer-optimizations](../2026-09-30-writer-optimizations/). Peers BCL/BC unchanged: [2026-09-23](../2026-09-23/).

## Decode KPI

| Method | 2026-10-09 | Now | Δ Mean | 2026-10-09 Alloc | Now | Δ Alloc |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| ★ Cert `Asn1Kit_Decode` | 6.773 µs | 4.841 µs | **−28.5%** | 2.91 KB | 1.57 KB | **−46%** |
| ★ CRL `Asn1Kit_Decode` | 4.836 µs | 3.799 µs | **−21.4%** | 1.93 KB | 1288 B | **−35%** |
| ★ CMS `Asn1Kit_Lazy_Decode` | 2.639 µs | 2.047 µs | **−22.4%** | 1.21 KB | 1.08 KB | **−11%** |

| Method | explicit-enter Mean | Now | explicit-enter Alloc | Now |
| --- | ---: | ---: | ---: | ---: |
| ★ Cert `Asn1Kit_Decode` | 3.252 µs | 4.841 µs | 1.77 KB | **1.57 KB** |
| ★ CRL `Asn1Kit_Decode` | 2.622 µs | 3.799 µs | 1.32 KB | **1288 B** |
| ★ CMS `Asn1Kit_Lazy_Decode` | 1.481 µs | 2.047 µs | 968 B | **1.08 KB** |

Cert/CRL Alloc is back at or below the 2026-09-24 opaque-AVA floor. Mean is still above that era (host noise + remaining retainEncoded / OF / OID work); Alloc is the durable signal.

### CMS Lazy Decode path

Fair peer to BCL `SignedCms.Decode` / BC `ContentInfo.GetInstance`:

1. `ContentInfo.Decode` (envelope; `Content` as opaque `Asn1Any`)
2. `TryDecodeContent(ContentInfoContentBindings.SignedData)` → typed `SignedData` shell
3. `CertificateChoices.certificate` stays lazy (no `.Value`)

Inventory (`--alloc-profile`): **1104 B** for the same path (matches BDN 1.08 KB).

### Inventory (`--alloc-profile`)

| Fixture | Allocated/op | Notes |
| --- | ---: | --- |
| TrustAnchorRootCertificate | 1912 B | 6× AVA `Asn1Any` + 3× alg-param `Asn1Any?` (no string materialize) |
| GoodCACRL | 1744 B | 3× AVA + 2× alg-param |
| attached-signeddata (lazy) | 1104 B | ContentInfo + SignedData shell; cert TLV lazy |

BDN Alloc for Cert (1.57 KB) is slightly under the one-shot inventory; same methodology gap as in the 2026-10-09 snapshot.

## Encode KPI

Hand-built Cert/CRL graphs use `ValueBinding.Create` / `ParametersBinding.Create` (pre-encoded `Asn1Any` payloads). CMS Lazy Encode re-binds typed `SignedData` via `SetContent` each op, then `ContentInfo.Encode` (structural SignedData encode + envelope; lazy cert still `WriteRaw`).

| Method | 2026-09-30 After | Now | Δ Mean | 2026-09-30 Alloc | Now |
| --- | ---: | ---: | ---: | ---: | ---: |
| ★ Cert `Asn1Kit_Encode` | 4.043 µs | 2.628 µs | **−35%** | 4.13 KB | **1.7 KB** |
| ★ CRL `Asn1Kit_Encode` | 3.548 µs | 2.244 µs | **−37%** | 2.82 KB | **976 B** |
| ★ CMS `Asn1Kit_Lazy_Encode` | 2.198 µs | 1.555 µs | **−29%** | 2.96 KB | **5.79 KB** |

Cert/CRL Encode Alloc↓ is the Binding model: AVA/parameters are already owned TLVs (`WriteAny` / framing). CMS Encode Alloc↑ vs writer-opts: `SetContent` materializes a full SignedData `Asn1Any` before envelope write (Binding API path); Mean is still down. Raw CMS report: [Asn1Kit.Benchmarks.CmsBenchmarks-report-github.md](Asn1Kit.Benchmarks.CmsBenchmarks-report-github.md); CMS-only re-run log: [BenchmarkRun-cms.log](BenchmarkRun-cms.log).

## Verdict

Unify onto Binding + opaque `Asn1Any` **recovers** most of the Decode Alloc regression from 2026-10-09 on Cert/CRL/CMS shell. Typed open-type cost moves to `TryDecode*` / `Set*` call sites (pay only when binding). CMS Lazy KPI includes SignedData materialization so it stays comparable to BCL/BC.
