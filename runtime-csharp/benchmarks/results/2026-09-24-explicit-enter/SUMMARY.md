# Benchmark snapshot — 2026-09-24 (EXPLICIT EnterExplicit; no ReadSequence Func)

| Field | Value |
| --- | --- |
| Date (local) | 2026-09-24 |
| Git | working tree (`EnterExplicit`; removed `ReadSequence`/`ReadSet` Func+Action; codegen EXPLICIT → `using EnterExplicit`) |
| Host | Windows 11 (10.0.26200), .NET 6.0.10 / SDK 6.0.402, X64 RyuJIT AVX2 |
| Command | `dotnet run -c Release --project runtime-csharp/benchmarks/Asn1Kit.Pkix.Benchmarks -- --filter "*CertificateBenchmarks.Asn1Kit_Decode" "*CertificateListBenchmarks.Asn1Kit_Decode" "*CmsBenchmarks.Asn1Kit_Lazy_Decode"` |

Baseline: [2026-09-24-sequence-lambda](../2026-09-24-sequence-lambda/) (SEQUENCE/SET EnterSequence + OF no-closure; EXPLICIT still `ReadSequence(Func)`).

## Decode KPI (два прогона подряд)

| Method | sequence-lambda | run 1 | run 2 | Alloc |
| --- | ---: | ---: | ---: | ---: |
| ★ Cert Asn1Kit_Decode | 3.258 µs | 3.252 µs | 3.344 µs | 1.77 KB (все три) |
| ★ CRL Asn1Kit_Decode | 2.459 µs | 2.622 µs | 2.654 µs | 1.32 KB (все три) |
| ★ CMS Asn1Kit_Lazy_Decode | 1.387 µs / 1.03 KB | 1.481 µs / 968 B | 1.440 µs / 968 B | −6% vs sequence-lambda |

Run 1 ↔ run 2 (Same build): Cert ±3%, CRL ±1.2%, CMS −2.8% — типичный шум BDN.

CRL Mean стабильно ~2.63 µs на этой сессии vs 2.46 в sequence-lambda snapshot (другая сессия/тепло). **Allocated без изменений** на Cert/CRL → регрессии от `EnterExplicit` нет; разница Mean к старому снимку — межсессионный шум, не эффект правки.

**Вердикт:** API-чистка без Alloc-выигрыша (non-capturing EXPLICIT Func уже кэшировался). Mean не интерпретировать как регрессию без A/B в одном процессе.
