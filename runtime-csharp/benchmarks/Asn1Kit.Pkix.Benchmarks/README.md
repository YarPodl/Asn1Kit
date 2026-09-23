# Asn1Kit.Pkix.Benchmarks

Сравнение Decode/Encode **Certificate**, **CertificateList** (CRL) и CMS **ContentInfo** (attached SignedData).

## Что сравнивается (важно)

Типы бенчмарка — [Asn1Kit.Pkix.Bench](../../generated/Asn1Kit.Pkix.Bench/) из golden `cms-2004.json` + [cms-2004-bench.patch.json](../../../compiler/fixtures/ir/cms-2004-bench.patch.json). Это **не** продуктовый golden [`Asn1Kit.Pkix`](../../generated/Asn1Kit.Pkix/) (eager).

| Путь | Глубина | Peer |
| --- | --- | --- |
| CMS `Asn1Kit_Lazy_*` | Bench + `options.lazy` на `CertificateChoices.certificate`; **без** `.Value` | BCL `SignedCms` shell (cert как opaque DER) |
| CMS `Asn1Kit_Lazy_Materialize_*` | то же + обход `Certificate.Value` | BCL + `Certificates` / `SignerInfos` |
| CMS `Asn1Kit_Eager_*` | golden `Asn1Kit.Cms` (полный typed `Certificate`) | полная глубина ASN.1 tree |
| CMS BouncyCastle | `Asn1Object` tree + `ContentInfo` | общий ASN.1 codec |
| Certificate / CRL Decode | полный typed decode | BCL Cert = PAL+lazy (другая модель); peer typed — BouncyCastle |
| Certificate / CRL Encode | **hand-built** object graph (PKITS-scale test data, no Decode) | BouncyCastle `GetEncoded` after assembly; BCL structural encode нет |

Фикстуры: [fixtures/pkix](../../fixtures/pkix/), [fixtures/cms](../../fixtures/cms/).

Не входит в `dotnet test`. Запуск:

```powershell
dotnet run -c Release --project runtime-csharp/benchmarks/Asn1Kit.Pkix.Benchmarks
```

**Краткий прогон** (по умолчанию после правок runtime/codegen на той же машине) — Cert/CRL `Asn1Kit_Decode` + CMS `Asn1Kit_Lazy_Decode`:

```powershell
dotnet run -c Release --project runtime-csharp/benchmarks/Asn1Kit.Pkix.Benchmarks -- --filter *Asn1Kit_Decode|*Asn1Kit_Lazy_Decode
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
| Asn1Kit Bench / Eager | Decode + Encode (hand-built) | Decode + Encode (hand-built) | Lazy / Materialize / Eager |
| BCL (`X509Certificate2` / `SignedCms`) | Decode only | — | Decode ± materialize; Encode |
| BouncyCastle | Decode + `GetEncoded` (hand-built) | Decode + `GetEncoded` (hand-built) | Decode + `GetEncoded` |

**Encode:** Cert/CRL объект собирается в `GlobalSetup` как hand-built граф (масштаб PKITS Trust Anchor / GoodCACRL; без Decode). В `[Benchmark]` только structural encode — без `WriteRaw` retained TLV. CMS Lazy Encode по-прежнему идёт от decoded tree — это shell-сценарий с `WriteRaw` cert.

Полный typed `Certificate.Decode` **не обязан** быть быстрее `X509Certificate2` (BCL не строит ASN-граф). Цель typed path — сравняться с BouncyCastle; Lazy/retainEncoded — peer BCL shell.

История: выбор стратегии заполнения SEQUENCE OF (`List` / ArrayPool / pre-count) закрыт микробенчем; прод — ArrayPool + empty/single. Снимок — [results/2026-09-23-of-arrays](../results/2026-09-23-of-arrays/).
Гипотеза лямбд в `ReadSequence`: выигрыш Alloc — от устранения capturing-обёртки в `ReadSequenceOf` (+ codegen `EnterSequence`); Nest Func→cursor почти не режет Alloc. Снимок — [results/2026-09-24-sequence-lambda](../results/2026-09-24-sequence-lambda/).

## Current baseline (2026-09-24, sequence-lambda)

Снимок после `EnterSequence` / OF без capturing-лямбды вокруг `decodeItem`. Сырые заметки: [results/2026-09-24-sequence-lambda](../results/2026-09-24-sequence-lambda/). Peers BCL/BC — из [results/2026-09-23](../results/2026-09-23/) (`7d281d1`).

Краткий набор для сравнения после правок отмечен ★.

### Certificate Decode history (Asn1Kit vs self)

Фикстура `TrustAnchorRootCertificate.crt` (Bench + retainEncoded).

| Stage | Mean | Allocated |
| --- | ---: | ---: |
| Baseline (before decode opts) | 4.246 µs | 6.16 KB |
| After Int32/Time + OID open-type + OF capacity | 3.436 µs | 2.84 KB |
| 2026-09-23 (`7d281d1`, full suite) | 3.352 µs | 2912 B |
| 2026-09-23 OF→arrays | 3.774 µs | 2600 B |
| **2026-09-24 sequence-lambda** | **3.258 µs** | **1.77 KB** |

### Certificate / CRL / CMS Asn1Kit (2026-09-24 sequence-lambda + codegen)

| Method | Mean | Allocated | vs of-arrays Alloc |
| --- | ---: | ---: | ---: |
| ★ Cert Asn1Kit_Decode | 3.258 µs | 1.77 KB | −30% |
| ★ CRL Asn1Kit_Decode | 2.459 µs | 1.32 KB | −34% |
| ★ CMS Asn1Kit_Lazy_Decode | 1.387 µs | 1.03 KB | −29% |

Encode не перезамерялся на этом прогоне (ожидается без изменений — Write-путь не трогали). Decode Allocated↓ за счёт OF без closure; Mean↓ после codegen `EnterSequence`.
