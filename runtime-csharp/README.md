# C# runtime (BER/DER)

Библиотека кодека: `Asn1Writer` / `Asn1Reader` и примитивы. Не знает про ASN.1-модули и IR — только TLV и правила кодирования.

Рядом лежит эталонный сгенерированный код PKIX ([generated/Asn1Kit.Pkix](generated/Asn1Kit.Pkix/)) — golden артефакт C# backend. Encode/decode тесты на нём ещё не написаны; мелкий round-trip через Roslyn остаётся в `compiler/tests`.

## Проекты

| Проект | Роль |
| --- | --- |
| `src/Asn1Kit.Runtime` | Теги, writer/reader, примитивы |
| `generated/Asn1Kit.Pkix` | Golden C# из PKIX1Explicit88 + PKIX1Implicit88; `*.g.cs` руками не править |
| `tests/Asn1Kit.Runtime.Tests` | Матрица hex, oracle BCL, внешние векторы, `RuntimeTests` |

## Фикстуры

`fixtures/ber-der/` — hex-матрица и `external/` (см. [fixtures/ber-der/README.md](fixtures/ber-der/README.md)).

Слои тестов:

1. `PrimitiveCodecTests` — своя матрица
2. `PrimitiveOracleTests` — `System.Formats.Asn1`
3. `ExternalVectorTests` — RFC / внешние фрагменты

## Документация

- [Ревью API](docs/runtime-api.md)
- [Плейбук runtime](docs/playbooks/runtime.md)
- Матрица поддержки — [docs/status.md](../docs/status.md) § Runtime BER/DER

```powershell
dotnet test Asn1Kit.sln --filter FullyQualifiedName~Asn1Kit.Runtime.Tests
```
