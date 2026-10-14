# Asn1Kit C++ benchmarks

Decode/Encode KPI для **Certificate**, **CertificateList** (CRL) и CMS **ContentInfo** (attached SignedData) на Modern generated API. Только Asn1Kit (peers вроде OpenSSL — вне scope).

Фикстуры: [runtime-csharp/fixtures/pkix](../../runtime-csharp/fixtures/pkix/), [runtime-csharp/fixtures/cms](../../runtime-csharp/fixtures/cms/).

**Не входит в** `scripts/test-runtime-cpp.ps1` / `ctest`. Опция `ASN1KIT_BUILD_BENCHMARKS` по умолчанию `OFF`.

## Сборка (FetchContent Release)

```powershell
cd runtime-cpp
cmake --preset fetch-release -DASN1KIT_BUILD_BENCHMARKS=ON
cmake --build --preset fetch-release --target asn1kit_benchmarks
```

## Сборка (Conan Release)

```powershell
cd runtime-cpp
conan install . -s build_type=Release -of build-release --build=missing -o "&:build_benchmarks=True"
cmake --preset conan-release
cmake --build --preset conan-release --target asn1kit_benchmarks
```

## Запуск

Перед таймингом binary всегда делает smoke (load/decode/encode фикстур + hand-built encode samples).

**Краткий KPI** (после правок runtime/codegen на той же машине):

```powershell
.\build-fetch-release\benchmarks\asn1kit_benchmarks.exe --benchmark_filter="Certificate_|CertificateList_|Cms_Lazy_(Decode|Encode)$"
```

**Полный suite** (включая `Cms_Lazy_Materialize_Decode`):

```powershell
.\build-fetch-release\benchmarks\asn1kit_benchmarks.exe
```

JSON-отчёт:

```powershell
.\build-fetch-release\benchmarks\asn1kit_benchmarks.exe --benchmark_filter="Certificate_|CertificateList_|Cms_Lazy_(Decode|Encode)$" --benchmark_out=bench.json --benchmark_out_format=json
```

## Что измеряется

| Benchmark | Setup | Hot path |
| --- | --- | --- |
| `Certificate_Decode` | `TrustAnchorRootCertificate.crt` | `asn1kit::decode<Certificate>` |
| `Certificate_Encode` | hand-built graph | structural encode (no Decode) |
| `CertificateList_Decode` | `GoodCACRL.crl` | decode CRL |
| `CertificateList_Encode` | hand-built CRL | structural encode |
| `Cms_Lazy_Decode` | `attached-signeddata.p7m` | ContentInfo + SignedData shell |
| `Cms_Lazy_Encode` | decoded SignedData | re-bind content + encode |
| `Cms_Lazy_Materialize_Decode` | same CMS | shell + invalidate/access retained TBS |

## Результаты

Архив снимков: [results/](results/). После прогона скопируйте console/JSON в `results/YYYY-MM-DD/` и добавьте `SUMMARY.md` (git SHA, host, filter, Mean).

## Current baseline

Пока нет checked-in снимка — первый Release-прогон зафиксируйте в `results/`.
