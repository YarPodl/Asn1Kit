# C# runtime (BER/DER)

Библиотека кодека: `Asn1Writer` / `Asn1Reader` и примитивы. Не знает про ASN.1-модули и IR — только TLV и правила кодирования.

Сгенерированные типы: product — [generated/Asn1Kit.Modern](generated/Asn1Kit.Modern/); регрессия ASN.1:1988 — [generated/Asn1Kit.Legacy](generated/Asn1Kit.Legacy/). Codec-тесты PKITS — [tests/Asn1Kit.Legacy.Tests](tests/Asn1Kit.Legacy.Tests/) с фикстурами [fixtures/pkix/](fixtures/pkix/).

## Проекты

| Проект | Роль |
| --- | --- |
| `src/Asn1Kit.Runtime` | Теги, writer/reader, примитивы |
| `generated/Asn1Kit.Modern` | Modern PKIX/CMS + GOST RFC 9215 + TSP (27 модулей) |
| `generated/Asn1Kit.Legacy` | Legacy ASN.1:1988 PKIX/CMS/DVCS (регрессия) |
| `tests/Asn1Kit.Runtime.Tests` | Матрица hex, oracle BCL, внешние векторы |
| `tests/Asn1Kit.Legacy.Tests` | Encode/decode Cert/CRL/CMS/DVCS + Demo |
| `benchmarks/Asn1Kit.Legacy.Benchmarks` | BenchmarkDotNet (не `dotnet test`) |
| `examples/Asn1Kit.Cms.Demo` | Demo; default Modern, `--legacy` для Legacy |

## Фикстуры

`fixtures/ber-der/` — hex-матрица и `external/` (см. [fixtures/ber-der/README.md](fixtures/ber-der/README.md)).

`fixtures/pkix/` — DER сертификатов/СОС NIST PKITS + `expected.json` из certutil/BCL (см. [fixtures/pkix/README.md](fixtures/pkix/README.md)).

`fixtures/cms/` — attached SignedData для бенчмарков (см. [fixtures/cms/README.md](fixtures/cms/README.md)).

## Документация

- [Публичный API](docs/runtime-api.md)
- [Плейбук runtime](docs/playbooks/runtime.md)
- Матрица поддержки — [docs/status.md](../docs/status.md)

```powershell
dotnet test Asn1Kit.sln --filter FullyQualifiedName~Asn1Kit.Runtime.Tests

dotnet run -c Release --project runtime-csharp/benchmarks/Asn1Kit.Legacy.Benchmarks
```
