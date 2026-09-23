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

**Сравнение оптимизаций на той же машине** — только Asn1Kit (BCL/BouncyCastle не гоняй: их Mean/Allocated стабильны относительно peer’ов; цифры peers — из полного baseline в [results/](../results/)):

```powershell
dotnet run -c Release --project runtime-csharp/benchmarks/Asn1Kit.Pkix.Benchmarks -- --filter *Asn1Kit*
```

Полный suite (Asn1Kit + BCL + BouncyCastle) — при смене машины/SDK или обновлении peer’ов:

```powershell
dotnet run -c Release --project runtime-csharp/benchmarks/Asn1Kit.Pkix.Benchmarks -- --filter *
```

Точечно:

```powershell
dotnet run -c Release --project runtime-csharp/benchmarks/Asn1Kit.Pkix.Benchmarks -- --filter *Cms*Asn1Kit*
dotnet run -c Release --project runtime-csharp/benchmarks/Asn1Kit.Pkix.Benchmarks -- --filter *CertificateBenchmarks.Asn1Kit_Decode*
```

| Library | Certificate | CRL | CMS |
| --- | --- | --- | --- |
| Asn1Kit Bench / Eager | Decode + Encode (hand-built) | Decode + Encode (hand-built) | Lazy / Materialize / Eager |
| BCL (`X509Certificate2` / `SignedCms`) | Decode only | — | Decode ± materialize; Encode |
| BouncyCastle | Decode + `GetEncoded` (hand-built) | Decode + `GetEncoded` (hand-built) | Decode + `GetEncoded` |

**Encode:** Cert/CRL объект собирается в `GlobalSetup` как hand-built граф (масштаб PKITS Trust Anchor / GoodCACRL; без Decode). В `[Benchmark]` только structural encode — без `WriteRaw` retained TLV. CMS Lazy Encode по-прежнему идёт от decoded tree — это shell-сценарий с `WriteRaw` cert.

Полный typed `Certificate.Decode` **не обязан** быть быстрее `X509Certificate2` (BCL не строит ASN-граф). Цель typed path — сравняться с BouncyCastle; Lazy/retainEncoded — peer BCL shell.

## Current baseline (2026-09-23, `7d281d1`)

Полный Release-прогон (`--filter *`). Сырые отчёты: [results/2026-09-23](../results/2026-09-23/). История снимков: [results/](../results/).

### Certificate Decode history (Asn1Kit vs self)

Фикстура `TrustAnchorRootCertificate.crt` (Bench + retainEncoded).

| Stage | Mean | Allocated |
| --- | ---: | ---: |
| Baseline (before decode opts) | 4.246 µs | 6.16 KB |
| After Int32/Time + OID open-type + OF capacity | 3.436 µs | 2.84 KB |
| **2026-09-23** (`7d281d1`, full suite) | **3.352 µs** | **2912 B** |

### Certificate / CRL / CMS (2026-09-23)

| Method | Mean | Allocated |
| --- | ---: | ---: |
| Cert Asn1Kit_Decode | 3.352 µs | 2912 B |
| Cert Bcl_Decode | 2.624 µs | 328 B |
| Cert BouncyCastle_Decode | 9.521 µs | 13280 B |
| Cert Asn1Kit_Encode | 2.353 µs | 4232 B |
| Cert BouncyCastle_Encode | 2.679 µs | 4792 B |
| CRL Asn1Kit_Decode | 2.634 µs | 2.31 KB |
| CRL BouncyCastle_Decode | 7.325 µs | 10.45 KB |
| CRL Asn1Kit_Encode | 2.089 µs | 2.82 KB |
| CRL BouncyCastle_Encode | 2.169 µs | 4.01 KB |
| CMS Asn1Kit_Lazy_Decode | 1.649 µs | 2.1 KB |
| CMS Asn1Kit_Lazy_Materialize_Decode | 4.059 µs | 4.05 KB |
| CMS Asn1Kit_Eager_Decode | 3.710 µs | 3.88 KB |
| CMS Bcl_Decode | 1.599 µs | 3.45 KB |
| CMS Bcl_Materialize_Decode | 8.676 µs | 4.68 KB |
| CMS BouncyCastle_Decode | 7.538 µs | 13.59 KB |
| CMS Asn1Kit_Lazy_Encode | 1.285 µs | 2.96 KB |
| CMS Asn1Kit_Eager_Encode | 2.764 µs | 4.45 KB |
| CMS Bcl_Encode | 1.967 µs | 8.85 KB |
| CMS BouncyCastle_Encode | 2.647 µs | 5.11 KB |

Lazy CMS Decode ≈ peer BCL shell (−3% Mean); ≈2.3× быстрее Eager; Allocated Lazy Decode 2.1 KB vs BCL 3.45 KB.
