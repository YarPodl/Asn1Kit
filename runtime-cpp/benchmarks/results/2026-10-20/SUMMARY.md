# Asn1Kit / OpenSSL C++ benchmark baseline — 2026-10-20

Первый сравнительный Release-снимок. Это характеристика указанной машины, а не performance gate.

## Environment

- Git base: `11d4e921ca57d76bea1a18144be5f2945ea9c21d` с незакоммиченными изменениями этого benchmark.
- Host: `HUAWEINOTEBOOK`, Windows 10 Home Single Language 25H2, build 26200.9445.
- CPU: 12th Gen Intel Core i5-12500H, 16 logical CPUs; Google Benchmark сообщил 3110 MHz.
- Compiler: MSVC 19.33.31630 x64, C++20; CMake 3.23.22060601-MSVC_2; Conan 2.33.0.
- Libraries: Google Benchmark 1.9.1; OpenSSL 3.5.7 (9 Jun 2026), static `OpenSSL::Crypto`.
- Fixtures: `TrustAnchorRootCertificate.crt` — 843 bytes; `GoodCACRL.crl` — 516 bytes; `attached-signeddata.p7m` — 1161 bytes.

## Command

```powershell
conan install . -s build_type=Release -s compiler.cppstd=20 -of build-release `
  --build=missing -o "&:build_benchmarks=True" `
  -c tools.cmake.cmaketoolchain:generator=Ninja
cmake --preset conan-release
cmake --build --preset conan-release --target asn1kit_benchmarks

.\build-release\benchmarks\asn1kit_benchmarks.exe `
  --benchmark_repetitions=5 `
  --benchmark_enable_random_interleaving=true `
  --benchmark_out=benchmarks/results/2026-10-20/bench.json `
  --benchmark_out_format=json
```

JSON содержит 13/13 ожидаемых сценариев, 13 aggregate mean и 0 записей с `error_occurred=true`.

## Aggregate mean

| Benchmark | Real time, us | CPU time, us | Real-time CV |
| --- | ---: | ---: | ---: |
| `Certificate_Asn1Kit_Decode` | 8.745 | 8.705 | 4.99% |
| `Certificate_Asn1Kit_Encode` | 2.727 | 2.704 | 3.71% |
| `Certificate_OpenSSL_Decode` | 65.187 | 65.848 | 1.83% |
| `Certificate_OpenSSL_Encode` | 1.591 | 1.604 | 3.72% |
| `CertificateList_Asn1Kit_Decode` | 5.554 | 5.553 | 27.69% |
| `CertificateList_Asn1Kit_Encode` | 1.931 | 1.924 | 21.42% |
| `CertificateList_OpenSSL_Decode` | 30.297 | 30.085 | 2.70% |
| `CertificateList_OpenSSL_Encode` | 1.593 | 1.590 | 4.73% |
| `Cms_Asn1Kit_Lazy_Decode` | 9.622 | 9.563 | 28.14% |
| `Cms_Asn1Kit_Lazy_Materialize_Decode` | 11.273 | 11.084 | 3.82% |
| `Cms_Asn1Kit_Lazy_Encode` | 3.774 | 3.754 | 2.96% |
| `Cms_OpenSSL_Decode` | 68.657 | 68.917 | 3.44% |
| `Cms_OpenSSL_Encode` | 12.429 | 12.333 | 1.46% |

У трёх Asn1Kit-сценариев CV выше 20%; их Mean следует считать ориентировочным и перепроверять на стабилизированном host перед выводами о регрессии. Cert/CRL encode между библиотеками использует разные входные графы, как описано в [benchmark README](../../README.md).
