# Benchmark snapshot — 2026-09-23 (ReadSequenceOf → T[])

| Field | Value |
| --- | --- |
| Date (local) | 2026-09-23 ~23:50 |
| Git | working tree (ReadSequenceOf `List<T>` → `T[]` / ArrayPool; backlog §8) |
| Command | `dotnet run -c Release --project runtime-csharp/benchmarks/Asn1Kit.Pkix.Benchmarks -- --filter *Asn1Kit*` (matches namespace; includes peers + SequenceOfFill) |
| Host | Windows 11 (10.0.26200), .NET 6.0.10 / SDK 6.0.402, X64 RyuJIT AVX2 |

Peers BCL/BC: эталон по-прежнему [2026-09-23](../2026-09-23/) (`7d281d1`); ниже — Asn1Kit после OF→arrays.

## SequenceOf fill microbench (A List / B ArrayPool / C pre-count)

Production ships **B + empty/`Array.Empty` + single-element `new T[1]`** (C dual-pass lost on Mean and Allocated for Many/Indefinite).

| Method | Category | Mean | Allocated | vs A Alloc |
| --- | --- | ---: | ---: | ---: |
| A_List | Empty | 52.8 ns | 80 B | 1.00 |
| B_Pool | Empty | 48.6 ns | 48 B | 0.60 |
| C_PreCount | Empty | 71.1 ns | 184 B | 2.30 |
| A_List | One | 95.8 ns | 120 B | 1.00 |
| B_Pool | One | 138.9 ns | 88 B | 0.73 |
| C_PreCount | One | 156.1 ns | 224 B | 1.87 |
| A_List | Many16 | 785 ns | 896 B | 1.00 |
| B_Pool | Many16 | 693 ns | 328 B | **0.37** |
| C_PreCount | Many16 | 953 ns | 464 B | 0.52 |
| A_List | Indefinite8 | 601 ns | 416 B | 1.00 |
| B_Pool | Indefinite8 | 548 ns | 200 B | **0.48** |
| C_PreCount | Indefinite8 | 687 ns | 336 B | 0.81 |

## Certificate (`TrustAnchorRootCertificate.crt`)

| Method | Mean | Allocated | vs `7d281d1` |
| --- | ---: | ---: | --- |
| Asn1Kit_Decode | 3.774 µs | **2600 B** | Mean +12%; Alloc **−11%** (2912→2600) |
| Asn1Kit_Encode | 2.464 µs | 4232 B | ≈same Alloc |

## CertificateList / CRL (`GoodCACRL.crl`)

| Method | Mean | Allocated | vs `7d281d1` |
| --- | ---: | ---: | --- |
| Asn1Kit_Decode | 3.010 µs | **2.01 KB** | Alloc **−13%** (2.31→2.01) |
| Asn1Kit_Encode | 2.190 µs | 2.82 KB | ≈same Alloc |

## CMS ContentInfo (attached SignedData)

| Method | Mean | Allocated | vs `7d281d1` |
| --- | ---: | ---: | --- |
| Asn1Kit_Lazy_Decode | 1.664 µs | **1.46 KB** | Alloc **−30%** (2.1→1.46) |
| Asn1Kit_Lazy_Materialize_Decode | 4.123 µs | **3.22 KB** | Alloc **−20%** (4.05→3.22) |
| Asn1Kit_Eager_Decode | 3.749 µs | **2.99 KB** | Alloc **−23%** (3.88→2.99) |
| Asn1Kit_Lazy_Encode | 1.367 µs | 2.96 KB | ≈same |
| Asn1Kit_Eager_Encode | 2.849 µs | 4.45 KB | ≈same |

Decode Allocated down across Cert/CRL/CMS; Encode unchanged (hand-built graphs / Write path). Mean Decode slightly noisier/higher on this run — primary win is allocation.
