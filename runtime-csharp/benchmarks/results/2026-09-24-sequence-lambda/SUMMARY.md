# Benchmark snapshot — 2026-09-24 (ReadSequenceOf no-closure + EnterSequence)

| Field | Value |
| --- | --- |
| Date (local) | 2026-09-24 |
| Git | working tree (`EnterSequence` / `EnterSet`; `ReadSequenceOf` without capturing wrap; codegen SEQUENCE/SET Decode → `using Enter*`) |
| Host | Windows 11 (10.0.26200), .NET 6.0.10 / SDK 6.0.402, X64 RyuJIT AVX2 |

Peers BCL/BC: [2026-09-23](../2026-09-23/) (`7d281d1`). До правки Asn1Kit: [2026-09-23-of-arrays](../2026-09-23-of-arrays/).

## Hypothesis

Отказ от лямбд в `ReadSequence` улучшит KPI. Статика: non-capturing SEQUENCE `Func` кэшируется (Alloc≈0); capturing-обёртка внутри `ReadSequenceOf` аллоцирует каждый вызов.

## SequenceLambda microbench (runtime OF fix; до codegen EnterSequence)

| Method | Category | Mean | Allocated | vs A Alloc |
| --- | --- | ---: | ---: | ---: |
| A_Func_NestedSequence | Nest | 247.0 ns | 48 B | 1.00 |
| B_Cursor_NestedSequence | Nest | 236.0 ns | 48 B | 1.00 |
| A_OfCapturing_Many16 | OfMany | 710.5 ns | 416 B | 1.00 |
| C_OfNoClosure_Many16 | OfMany | 688.5 ns | **328 B** | **0.79** |
| A_OfCapturing_One | OfOne | 99.5 ns | 176 B | 1.00 |
| C_OfNoClosure_One | OfOne | 88.8 ns | **88 B** | **0.50** |

Nest: cursor чуть быстрее Mean, Allocated одинаков. OF: без closure — Alloc↓ (−21% / −50%).

## Decode KPI

| Method | Stage | Mean | Allocated | vs of-arrays Alloc |
| --- | --- | ---: | ---: | --- |
| ★ Cert Asn1Kit_Decode | runtime OF only | 4.007 µs | 1.77 KB | −30% |
| ★ Cert Asn1Kit_Decode | **+ codegen EnterSequence** | **3.258 µs** | **1.77 KB** | −30%; Mean лучше of-arrays |
| ★ CRL Asn1Kit_Decode | runtime OF only | 2.845 µs | 1.32 KB | −34% |
| ★ CRL Asn1Kit_Decode | **+ codegen** | **2.459 µs** | **1.32 KB** | −34% |
| ★ CMS Asn1Kit_Lazy_Decode | runtime OF only | 1.595 µs | 1.03 KB | −29% |
| ★ CMS Asn1Kit_Lazy_Decode | **+ codegen** | **1.387 µs** | **1.03 KB** | −29% |

**Вердикт:** гипотеза подтверждена. Главный Alloc↓ — устранение capturing-обёртки в `ReadSequenceOf`. Codegen `EnterSequence` для SEQUENCE/SET Decode дополнительно улучшает Mean при том же Allocated.
