# Asn1Kit.Pkix.Benchmarks

Сравнение Decode/Encode **Certificate**, **CertificateList** (CRL) и CMS **ContentInfo** (attached SignedData).

## Что сравнивается (важно)

Типы бенчмарка — продуктовый [`Asn1Kit.Pkix`](../../generated/Asn1Kit.Pkix/) с options из [dvcs.patch.json](../../../compiler/fixtures/ir/dvcs.patch.json).

| Путь | Глубина | Peer |
| --- | --- | --- |
| CMS `Asn1Kit_Lazy_*` | `ContentInfo` + `TryDecodeContent(SignedData)`; `options.lazy` на cert; **без** `.Value` | BCL `SignedCms` shell (cert как opaque DER) |
| CMS `Asn1Kit_Lazy_Materialize_*` | то же + обход `Certificate.Value` | BCL + `Certificates` / `SignerInfos` |
| CMS BouncyCastle | `Asn1Object` tree + `ContentInfo` | общий ASN.1 codec |
| Certificate / CRL Decode | полный typed decode | BCL Cert = PAL+lazy (другая модель); peer typed — BouncyCastle |
| Certificate / CRL Encode | **hand-built** object graph (PKITS-scale test data, no Decode) | BouncyCastle `GetEncoded` after assembly; BCL structural encode нет |

Фикстуры: [fixtures/pkix](../../fixtures/pkix/), [fixtures/cms](../../fixtures/cms/).

Не входит в `dotnet test`. Запуск:

```powershell
dotnet run -c Release --project runtime-csharp/benchmarks/Asn1Kit.Pkix.Benchmarks
```

**Краткий прогон** (по умолчанию после правок runtime/codegen на той же машине) — Cert/CRL Decode+Encode + CMS Lazy Decode+Encode (в PowerShell задавай фильтры отдельными аргументами):

```powershell
dotnet run -c Release --project runtime-csharp/benchmarks/Asn1Kit.Pkix.Benchmarks -- --filter "*CertificateBenchmarks.Asn1Kit_Decode" "*CertificateBenchmarks.Asn1Kit_Encode" "*CertificateListBenchmarks.Asn1Kit_Decode" "*CertificateListBenchmarks.Asn1Kit_Encode" "*CmsBenchmarks.Asn1Kit_Lazy_Decode" "*CmsBenchmarks.Asn1Kit_Lazy_Encode"
```

**Инвентарь аллокаций** (без BDN; считает AVA/open-type/строки на тех же фикстурах):

```powershell
dotnet run -c Release --project runtime-csharp/benchmarks/Asn1Kit.Pkix.Benchmarks -- --alloc-profile
```

**Asn1Kit полный** (все 9 методов, без peers; peers — из [results/](../results/)):

```powershell
dotnet run -c Release --project runtime-csharp/benchmarks/Asn1Kit.Pkix.Benchmarks -- --filter *Asn1Kit_*
```

**Полный suite** (Asn1Kit + BCL + BouncyCastle) — при смене машины/SDK или обновлении peer’ов:

```powershell
dotnet run -c Release --project runtime-csharp/benchmarks/Asn1Kit.Pkix.Benchmarks -- --filter *
```

| Library | Certificate | CRL | CMS |
| --- | --- | --- | --- |
| Asn1Kit | Decode + Encode (hand-built) | Decode + Encode (hand-built) | Lazy / Materialize |
| BCL (`X509Certificate2` / `SignedCms`) | Decode only | — | Decode ± materialize; Encode |
| BouncyCastle | Decode + `GetEncoded` (hand-built) | Decode + `GetEncoded` (hand-built) | Decode + `GetEncoded` |

**Encode:** Cert/CRL объект собирается в `GlobalSetup` как hand-built граф через `Binding.Create` (масштаб PKITS Trust Anchor / GoodCACRL; без Decode). В `[Benchmark]` только structural encode — без `WriteRaw` retained TLV. CMS Lazy Encode каждый op делает `SetContent(SignedData)` + `ContentInfo.Encode` (structural SignedData, lazy cert — `WriteRaw`).

Полный typed `Certificate.Decode` **не обязан** быть быстрее `X509Certificate2` (BCL не строит ASN-граф). Цель typed path — сравняться с BouncyCastle; Lazy — peer BCL shell, а `retainEncoded` сохраняет исходные TLV для хеширования и проверки подписи.

История: выбор стратегии заполнения SEQUENCE OF (`List` / ArrayPool / pre-count) закрыт микробенчем; прод — ArrayPool + empty/single. Снимок — [results/2026-09-23-of-arrays](../results/2026-09-23-of-arrays/).
Гипотеза лямбд в `ReadSequence`: выигрыш Alloc — от устранения capturing-обёртки в `ReadSequenceOf` (+ codegen `EnterSequence`); Nest Func→cursor почти не режет Alloc. Снимок — [results/2026-09-24-sequence-lambda](../results/2026-09-24-sequence-lambda/).
EXPLICIT без лямбд: `EnterExplicit` + удаление `ReadSequence(Func)`; Alloc на Cert/CRL без изменений (кэш делегата). Снимок — [results/2026-09-24-explicit-enter](../results/2026-09-24-explicit-enter/).
Writer reserve-one + однобайтовый `Asn1Integer` lookup: короткие/nested constructed быстрее на 32–44%, фабрики малых INTEGER дают 0 B/op; end-to-end Encode быстрее на 13–22%. Снимок — [results/2026-09-30-writer-optimizations](../results/2026-09-30-writer-optimizations/).

## Encode baseline (2026-09-30, writer optimizations)

Same-session `ShortRun` до/после production-правок; полные отчёты и оговорки — в [SUMMARY.md](../results/2026-09-30-writer-optimizations/SUMMARY.md).

| Method | Before | After | Change | Allocated |
| --- | ---: | ---: | ---: | ---: |
| Certificate Asn1Kit_Encode | 4.878 µs | 4.043 µs | −17.1% | 4.13 KB |
| CRL Asn1Kit_Encode | 4.236 µs | 3.548 µs | −16.2% | 2.82 KB |
| CMS Asn1Kit_Lazy_Encode | 2.523 µs | 2.198 µs | −12.9% | 2.96 KB |
| CMS Asn1Kit_Eager_Encode | 5.856 µs | 4.548 µs | −22.3% | 4.45 KB |

## Current baseline (2026-10-10, Decode+Encode after Binding unify)

Краткий Decode+Encode KPI после unify legacy open types на Binding/`Asn1Any`. Сырые отчёты: [results/2026-10-10-decode-encode-kpi](../results/2026-10-10-decode-encode-kpi/). Регрессия 2026-10-09 (eager wrappers): [results/2026-10-09-decode-kpi](../results/2026-10-09-decode-kpi/). Peers BCL/BC — из [results/2026-09-23](../results/2026-09-23/) (`7d281d1`).

Краткий набор для сравнения после правок отмечен ★. Инвентарь аллокаций: `--alloc-profile`.

### Certificate Decode history (Asn1Kit vs self)

Фикстура `TrustAnchorRootCertificate.crt` (retainEncoded на product options).

| Stage | Mean | Allocated |
| --- | ---: | ---: |
| Baseline (before decode opts) | 4.246 µs | 6.16 KB |
| After Int32/Time + OID open-type + OF capacity | 3.436 µs | 2.84 KB |
| 2026-09-23 (`7d281d1`, full suite) | 3.352 µs | 2912 B |
| 2026-09-23 OF→arrays | 3.774 µs | 2600 B |
| 2026-09-24 sequence-lambda | 3.258 µs | 1.77 KB |
| 2026-09-24 explicit-enter | 3.252 µs | 1.77 KB |
| 2026-10-09 open-type KPI (eager wrappers) | 6.773 µs | 2.91 KB |
| **2026-10-10 Binding unify** | **4.841 µs** | **1.57 KB** |

### Certificate / CRL / CMS Asn1Kit (2026-10-10)

| Method | Mean | Allocated | vs 2026-10-09 Alloc |
| --- | ---: | ---: | ---: |
| ★ Cert Asn1Kit_Decode | 4.841 µs | 1.57 KB | **−46%** |
| ★ CRL Asn1Kit_Decode | 3.799 µs | 1288 B | **−35%** |
| ★ CMS Asn1Kit_Lazy_Decode | 2.047 µs | 1.08 KB | **−11%** (ContentInfo + SignedData shell) |
| ★ Cert Asn1Kit_Encode | 2.628 µs | 1.7 KB | vs writer-opts 4.13 KB |
| ★ CRL Asn1Kit_Encode | 2.244 µs | 976 B | vs writer-opts 2.82 KB |
| ★ CMS Asn1Kit_Lazy_Encode | 1.555 µs | 5.79 KB | Mean↓; Alloc↑ из‑за `SetContent` |

Decode Alloc на Cert/CRL вернулся к уровню opaque-AVA (лучше explicit-enter). CMS Lazy KPI честно поднимает `SignedData` через Binding (peer BCL/BC); cert остаётся lazy.
