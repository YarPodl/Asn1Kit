# Asn1Kit C++ benchmarks

Decode/Encode KPI для **Certificate**, **CertificateList** (CRL) и CMS **ContentInfo** (attached SignedData): Asn1Kit Modern generated API сравнивается с OpenSSL 3.5+.

Фикстуры: [runtime-csharp/fixtures/pkix](../../runtime-csharp/fixtures/pkix/), [runtime-csharp/fixtures/cms](../../runtime-csharp/fixtures/cms/). Большая CRL размером около 40 MiB не хранится на диске: она один раз строится и DER-кодируется в памяти процесса из 1 165 000 записей, каждая с единственным `CRLReason=keyCompromise`.

**Не входит в** `scripts/test-runtime-cpp.ps1` / `ctest`. Опция `ASN1KIT_BUILD_BENCHMARKS` по умолчанию `OFF`; обычные runtime/tests от OpenSSL не зависят. При включении benchmark OpenSSL обязателен и линкуется только через `OpenSSL::Crypto`.

## Сборка (FetchContent Release + системный OpenSSL)

Установите OpenSSL 3.5+ так, чтобы `find_package(OpenSSL 3.5 COMPONENTS Crypto)` мог его найти. При нестандартном расположении передайте `-DOPENSSL_ROOT_DIR=<path>`.

```powershell
cd runtime-cpp
cmake --preset fetch-release -DASN1KIT_BUILD_BENCHMARKS=ON -DOPENSSL_ROOT_DIR=<path>
cmake --build --preset fetch-release --target asn1kit_benchmarks
```

## Сборка (Conan Release)

```powershell
cd runtime-cpp
conan install . -s build_type=Release -s compiler.cppstd=20 -of build-release --build=missing -o "&:build_benchmarks=True" -c tools.cmake.cmaketoolchain:generator=Ninja
cmake --preset conan-release
cmake --build --preset conan-release --target asn1kit_benchmarks
```

## Запуск

Перед таймингом binary всегда делает smoke: оба движка полностью декодируют fixture и воспроизводят исходный DER, дополнительно проверяются hand-built encode samples Asn1Kit. В JSON context записывается фактическая версия OpenSSL.

**Краткий KPI** (после правок runtime/codegen на той же машине):

```powershell
.\build-fetch-release\benchmarks\asn1kit_benchmarks.exe --benchmark_filter="(Certificate|CertificateList|LargeCertificateList)_(Asn1Kit|OpenSSL)_(Decode|Encode)$|Cms_(Asn1Kit_Lazy|OpenSSL)_(Decode|Encode)$"
```

**Только большая CRL**:

```powershell
.\build-fetch-release\benchmarks\asn1kit_benchmarks.exe --benchmark_filter="LargeCertificateList_(Asn1Kit|OpenSSL)_Decode$"
```

**Полный suite** (15 сценариев, включая `Cms_Asn1Kit_Lazy_Materialize_Decode`):

```powershell
.\build-fetch-release\benchmarks\asn1kit_benchmarks.exe
```

JSON-отчёт:

```powershell
.\build-fetch-release\benchmarks\asn1kit_benchmarks.exe --benchmark_repetitions=5 --benchmark_out=bench.json --benchmark_out_format=json
```

## Что измеряется

| Benchmark | Setup | Hot path |
| --- | --- | --- |
| `Certificate_Asn1Kit_Decode` | `TrustAnchorRootCertificate.crt` | `asn1kit::decode<Certificate>` + уничтожение объекта |
| `Certificate_Asn1Kit_Encode` | hand-built graph | structural encode + выделение выходного vector |
| `Certificate_OpenSSL_Decode` | тот же certificate fixture | `d2i_X509` + `X509_free` |
| `Certificate_OpenSSL_Encode` | заранее декодированный fixture | `i2d_X509` + `OPENSSL_free` |
| `CertificateList_Asn1Kit_Decode` | `GoodCACRL.crl` | decode CRL + уничтожение объекта |
| `CertificateList_Asn1Kit_Encode` | hand-built CRL | structural encode + выделение выходного vector |
| `CertificateList_OpenSSL_Decode` | тот же CRL fixture | `d2i_X509_CRL` + `X509_CRL_free` |
| `CertificateList_OpenSSL_Encode` | заранее декодированный fixture | `i2d_X509_CRL` + `OPENSSL_free` |
| `LargeCertificateList_Asn1Kit_Decode` | сгенерированная в памяти CRL около 40 MiB | полное декодирование 1 165 000 записей + уничтожение объекта |
| `LargeCertificateList_OpenSSL_Decode` | тот же сгенерированный DER | `d2i_X509_CRL` + `X509_CRL_free` |
| `Cms_Asn1Kit_Lazy_Decode` | `attached-signeddata.p7m` | ContentInfo + SignedData shell |
| `Cms_Asn1Kit_Lazy_Encode` | decoded SignedData | re-bind content + encode |
| `Cms_Asn1Kit_Lazy_Materialize_Decode` | тот же CMS | shell + invalidate/access retained TBS |
| `Cms_OpenSSL_Decode` | тот же CMS fixture | `d2i_CMS_ContentInfo` + `CMS_ContentInfo_free` |
| `Cms_OpenSSL_Encode` | заранее декодированный fixture | `i2d_CMS_ContentInfo` + `OPENSSL_free` |

Certificate/CRL encode не является строгим apples-to-apples сравнением: Asn1Kit сохраняет исторический hand-built workload, а OpenSSL кодирует объект из fixture. Decode использует одинаковые входные DER. CMS отражает разные модели библиотек: lazy shell Asn1Kit и штатный `CMS_ContentInfo` OpenSSL.

## Результаты

Архив снимков: [results/](results/). После прогона скопируйте JSON в `results/YYYY-MM-DD/` и добавьте `SUMMARY.md` (git SHA, host, toolchain, версии зависимостей, команда, размеры fixture и агрегированный Mean).

## Current baseline

Текущий baseline: [2026-10-20](results/2026-10-20/SUMMARY.md). Снимок характеризует только указанную машину и не является performance gate.
