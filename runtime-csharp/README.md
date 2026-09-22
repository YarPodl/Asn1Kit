# C# runtime (BER/DER)

Библиотека кодека: `Asn1Writer` / `Asn1Reader` и примитивы. Не знает про ASN.1-модули и IR — только TLV и правила кодирования.

Рядом лежит эталонный сгенерированный код PKIX/CMS ([generated/Asn1Kit.Pkix](generated/Asn1Kit.Pkix/)) — golden артефакт C# backend (`Asn1Kit.Pkix` + `Asn1Kit.Cms`). Encode/decode на PKIX — в [tests/Asn1Kit.Pkix.Tests](tests/Asn1Kit.Pkix.Tests/) с фикстурами [fixtures/pkix/](fixtures/pkix/) (NIST PKITS). Мелкий round-trip через Roslyn остаётся в `compiler/tests`.

## Проекты

| Проект | Роль |
| --- | --- |
| `src/Asn1Kit.Runtime` | Теги, writer/reader, примитивы |
| `generated/Asn1Kit.Pkix` | Golden C# PKIX1Explicit88 + PKIX1Implicit88 + CryptographicMessageSyntax2004; `*.g.cs` руками не править |
| `tests/Asn1Kit.Runtime.Tests` | Матрица hex, oracle BCL, внешние векторы, `RuntimeTests` |
| `tests/Asn1Kit.Pkix.Tests` | Encode/decode `Certificate` / `CertificateList` на NIST PKITS |
| `benchmarks/Asn1Kit.Pkix.Benchmarks` | BenchmarkDotNet: Decode/Encode Cert/CRL/CMS vs BCL и BouncyCastle (не `dotnet test`) |

## Фикстуры

`fixtures/ber-der/` — hex-матрица и `external/` (см. [fixtures/ber-der/README.md](fixtures/ber-der/README.md)).

`fixtures/pkix/` — DER сертификатов/СОС NIST PKITS + `expected.json` из certutil/BCL (см. [fixtures/pkix/README.md](fixtures/pkix/README.md)).

`fixtures/cms/` — attached SignedData для бенчмарков (см. [fixtures/cms/README.md](fixtures/cms/README.md)).

Слои тестов:

1. `PrimitiveCodecTests` — своя матрица
2. `PrimitiveOracleTests` — `System.Formats.Asn1`
3. `ExternalVectorTests` — RFC / внешние фрагменты

## Документация

- [Публичный API](docs/runtime-api.md)
- [Плейбук runtime](docs/playbooks/runtime.md)
- Матрица поддержки — [docs/status.md](../docs/status.md); API Writer/Reader — [docs/runtime-api.md](docs/runtime-api.md)

```powershell
dotnet test Asn1Kit.sln --filter FullyQualifiedName~Asn1Kit.Runtime.Tests

# бенчмарки Cert/CRL/CMS (не входят в dotnet test)
dotnet run -c Release --project runtime-csharp/benchmarks/Asn1Kit.Pkix.Benchmarks
```
