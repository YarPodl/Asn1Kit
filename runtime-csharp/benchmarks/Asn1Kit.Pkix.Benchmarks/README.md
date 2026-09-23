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

Точечно (после smoke):

```powershell
dotnet run -c Release --project runtime-csharp/benchmarks/Asn1Kit.Pkix.Benchmarks -- --filter *Cms*
```

Только Asn1Kit Cert Decode:

```powershell
dotnet run -c Release --project runtime-csharp/benchmarks/Asn1Kit.Pkix.Benchmarks -- --filter *CertificateBenchmarks.Asn1Kit_Decode*
```

| Library | Certificate | CRL | CMS |
| --- | --- | --- | --- |
| Asn1Kit Bench / Eager | Decode + Encode (hand-built) | Decode + Encode (hand-built) | Lazy / Materialize / Eager |
| BCL (`X509Certificate2` / `SignedCms`) | Decode only | — | Decode ± materialize; Encode |
| BouncyCastle | Decode + `GetEncoded` (hand-built) | Decode + `GetEncoded` (hand-built) | Decode + `GetEncoded` |

**Encode:** Cert/CRL объект собирается в `GlobalSetup` как hand-built граф (масштаб PKITS Trust Anchor / GoodCACRL; без Decode). В `[Benchmark]` только structural encode — без `WriteRaw` retained TLV. CMS Lazy Encode по-прежнему идёт от decoded tree — это shell-сценарий с `WriteRaw` cert.

Полный typed `Certificate.Decode` **не обязан** быть быстрее `X509Certificate2` (BCL не строит ASN-граф). Цель typed path — сравняться с BouncyCastle; Lazy/retainEncoded — peer BCL shell.

## Certificate Decode snapshot (Asn1Kit vs self)

Release, `*CertificateBenchmarks.Asn1Kit_Decode*`, фикстура `TrustAnchorRootCertificate.crt` (Bench + retainEncoded).

| Stage | Mean | Allocated |
| --- | ---: | ---: |
| Baseline (before decode opts) | 4.246 µs | 6.16 KB |
| After Int32/Time + OID open-type + OF capacity | 3.436 µs | 2.84 KB |

≈19% быстрее Mean, ≈54% меньше Allocated.

## CMS snapshot (local, after lazy Bench + writer buffer)

Representative run on this machine (Release, `*Cms*`). Baseline = `Asn1Kit_Lazy_*`.

| Method | Mean | Allocated |
| --- | ---: | ---: |
| Asn1Kit_Lazy_Decode | 2.07 µs | 6.1 KB |
| Asn1Kit_Lazy_Materialize_Decode | 4.80 µs | 10.6 KB |
| Asn1Kit_Eager_Decode | 4.70 µs | 10.5 KB |
| Bcl_Decode | 1.61 µs | 3.5 KB |
| Bcl_Materialize_Decode | 8.93 µs | 4.7 KB |
| BouncyCastle_Decode | 7.58 µs | 13.6 KB |
| Asn1Kit_Lazy_Encode | 2.15 µs | 2.9 KB |
| Asn1Kit_Eager_Encode | 4.37 µs | 4.5 KB |
| Bcl_Encode | 1.91 µs | 8.9 KB |
| BouncyCastle_Encode | 2.52 µs | 5.1 KB |

Lazy decode ≈ 2.3× быстрее Eager на той же фикстуре; Lazy encode ≈ 2× быстрее Eager (WriteRaw cert TLV). vs BCL shell Lazy всё ещё чуть медленнее на Decode, но заметно дешевле BCL+materialize.
