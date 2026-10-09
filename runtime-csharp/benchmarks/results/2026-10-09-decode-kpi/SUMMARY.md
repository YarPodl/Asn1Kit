# Benchmark snapshot — 2026-10-09 (Decode KPI after open-type bindings)

| Field | Value |
| --- | --- |
| Date (local) | 2026-10-09 |
| Git | `5180296` (+ working tree; Decode KPI uses product `Asn1Kit.Pkix`) |
| Host | Windows 11 (10.0.26200), .NET 6.0.10 / SDK 6.0.402, X64 RyuJIT AVX2 |
| Command | `dotnet run -c Release --project runtime-csharp/benchmarks/Asn1Kit.Pkix.Benchmarks -- --filter "*CertificateBenchmarks.Asn1Kit_Decode" "*CertificateListBenchmarks.Asn1Kit_Decode" "*CmsBenchmarks.Asn1Kit_Lazy_Decode"` |
| Alloc profile | `dotnet run -c Release --project runtime-csharp/benchmarks/Asn1Kit.Pkix.Benchmarks -- --alloc-profile` → [alloc-profile.txt](alloc-profile.txt) |

Peers BCL/BC: [2026-09-23](../2026-09-23/) (`7d281d1`). Previous Decode KPI baseline: [2026-09-24-explicit-enter](../2026-09-24-explicit-enter/).

## Decode KPI vs explicit-enter

| Method | explicit-enter Mean | Now | Δ Mean | explicit-enter Alloc | Now | Δ Alloc |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| ★ Cert `Asn1Kit_Decode` | 3.252 µs | 6.773 µs | +108% | 1.77 KB | 2.91 KB | **+64%** |
| ★ CRL `Asn1Kit_Decode` | 2.622 µs | 4.836 µs | +84% | 1.32 KB | 1.93 KB | **+46%** |
| ★ CMS `Asn1Kit_Lazy_Decode` | 1.481 µs | 2.639 µs | +78% | 968 B | 1.21 KB | **+28%** |

Allocated is the durable signal; Mean roughly tracks it on the same host/SDK.

## Root cause

Between the 2026-09-24 baseline and this run, typed PKIX open-type bindings landed (`e786d19` and follow-ups). Bench-era `AttributeTypeAndValue.Value` was opaque `Asn1Any` (`reader.ReadAny()`, no heap for the value). Product decode now builds:

1. **`AttributeTypeAndValue_Value`** (sealed class) per AVA — eager string decode via `ReadString`
2. **`AlgorithmIdentifier_Parameters`** (sealed class) when parameters are present (often NULL → still a wrapper)

### Inventory (`--alloc-profile`)

| Fixture | Allocated/op | Open-type values | Strings | Alg-param objs | Est. delta vs Asn1Any era |
| --- | ---: | ---: | ---: | ---: | ---: |
| TrustAnchorRootCertificate | 3288 B | 6 (issuer+subject) | 6 / 304 B | 3 | **+952 B** |
| GoodCACRL | 2432 B | 3 (issuer) | 3 / 144 B | 2 | **+496 B** |
| attached-signeddata (lazy) | 1240 B | 1 (SID issuer) | 1 / 56 B | — | matches CMS Alloc |

Estimated open-type delta accounts for most of the Alloc regression vs explicit-enter (+1.14 KB Cert / +0.61 KB CRL / +0.27 KB CMS). Remaining slack is layout estimate noise and secondary objects (`Asn1Value<>` retainEncoded shells, OF arrays, etc.).

Mean cost is the same path: per-AVA OID `Equals` chains in `AttributeTypeAndValue_Value.Decode` plus string materialization (baseline only compared TLV slices).

## Verdict

**Expected cost of typed open-type materialization on the RDN/algorithm hot path**, not a random runtime regression and not caused by Modern-only wrapper work. Sequence/EnterSequence Alloc wins from 2026-09-24 are still present underneath; open-type wrappers re-spent them on Cert and partially on CRL/CMS.

Mitigations to consider (not done here): keep unresolved/lazy open types as `Asn1Any` until `.Value` / `AsString`; dictionary/jump-table OID dispatch instead of long `Equals` OR-chains; struct open-type holders where alternatives are primitives.
