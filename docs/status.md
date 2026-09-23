# Что поддержано сейчас

Срез по слоям. Обновляется тем же изменением, которым расширяется поддержка.

Детали API runtime — [runtime-api.md](../runtime-csharp/docs/runtime-api.md); почему так — [decisions.md](decisions.md); IR — [ir-schema.md](ir-schema.md).

## Типы IR

| `kind` | Компилятор | C# backend | Тесты |
| --- | --- | --- | --- |
| `boolean` | да | `bool` | `ParserTests`, `PrimitiveCodecTests`, `PrimitiveOracleTests` |
| `integer` | да, `namedValues` | `int`…`Asn1Integer` по опции / выводу | `CompilerTests`, `Primitive*`, `RoundTripTests` |
| `enumerated` | да | C# `enum`; inline → `Owner_Field` | `PkixImplicit88Tests`, `RoundTripTests`, `PrimitiveCodecTests` |
| `bitString` | да, `namedBits` | `Asn1BitString` или класс + `[Flags]` | `Primitive*`, `RuntimeTests`, `RoundTripTests` |
| `octetString` | да | `ReadOnlyMemory<byte>` | `Primitive*`, `RoundTripTests`, `RuntimeTests` |
| `oid` | да (dotted; base-128 в т.ч. `2.999…`) | `Asn1Oid` — единственный codec (string↔arcs↔contents); dotted `string` — warm (`Encode`/`DecodeString`) | `Primitive*`, `RuntimeTests` |
| `string` (12 форм) | да | `string` + `Asn1StringForm` | `Primitive*` (все 12), `RuntimeTests`, `RoundTripTests` |
| `time` (`utc` / `generalized`) | да; `fractionDigits` 0…7 | `DateTimeOffset` + `Asn1TimeForm` | `Primitive*`, `RuntimeTests`, `RoundTripTests` |
| `any` (+ `definedBy`) | да; `bindings` через overlay | `Asn1Any` или `Owner_Field`; mismatch soft/strict | `OpenTypeBindingsTests`, `RuntimeTests`, `RoundTripTests` |
| `sequence` | да, `extensible` | класс или `struct` (`options.csharp.valueType`); `lazy` → `Asn1Lazy<T>`; `retainEncoded` → `Asn1Retained<T>` | `RoundTripTests`, `Pkix*`, `Asn1Kit.Pkix.Tests` |
| `set` | да | класс/`struct`; DER-порядок по тегу; `lazy` / `retainEncoded` | `RoundTripTests`, `ParserTests`, `RuntimeTests` |
| `choice` | да | `…Kind` + `From…`; однотипные → `Kind`+`Value`; один вариант → алиас | `ParserTests`, `Pkix*`, `RoundTripTests`, `Asn1Kit.Pkix.Tests` |
| `sequenceOf` | да | `T[]` (typedef сворачивается); `lazy` / `retainEncoded` на OF | `ParserTests`, `RoundTripTests`, `PkixGeneratedCodeTests`, `RuntimeTests` |
| `setOf` | да | `T[]` + DER-сортировка в runtime; `lazy` / `retainEncoded` | `RoundTripTests`, `ParserTests`, `RuntimeTests` |
| `ref` | да (`IMPORTS`) | алиасы сворачиваются; cross-module → квалиф. имя | `Pkix*`, `ImportResolutionTests`, `RoundTripTests` |

Неподдержанный kind → отказ до генерации (`EnsureBackendSupport`).

## Значения, теги, constraints

- Значения IR: `integer`, `boolean`, `null`, `oid`, `string`, `bitString`, `ref` (в скомпилированном IR обычно раскрыт).
- `EXPLICIT` / `IMPLICIT` / `AUTOMATIC TAGS`; `IMPORTS` между переданными файлами.
- Open-type `bindings`: CLI `--bindings` / overlay; ключи `Module.Type.field`.
- Options patch: CLI `--patch` / `IrOptionsPatch`; `modules`, `fields` (`Module.Type.field`), `types` (`Module.Type`); bench — [cms-2004-bench.patch.json](../compiler/fixtures/ir/cms-2004-bench.patch.json).
- `SIZE` / диапазоны → `constraint.size` / `constraint.value`; прочее → `constraint.unsupported`.
- Вне профиля (явный `CompileException`): `CLASS`/IOC, `COMPONENTS OF`, `REAL`, `EXTERNAL`, параметризованные типы.

## Runtime BER/DER

Полный инвентарь `Write*` / `Read*` и ownership — [runtime-api.md](../runtime-csharp/docs/runtime-api.md). Hex-матрица — [ber-der/](../runtime-csharp/fixtures/ber-der/); oracle BCL — `PrimitiveOracleTests`.

**Запись** всегда канонический DER. **Чтение** — soft-profile ([decisions.md](decisions.md), инвентарь опций — [runtime-api.md](../runtime-csharp/docs/runtime-api.md)):

| Soft-accept (default) | Strict-флаг |
| --- | --- |
| Non-minimal INTEGER contents | `RejectNonMinimalInteger` |
| BIT STRING nonzero trailing bits | `RejectBitStringTrailingBits` |

Не soft (default reject): non-minimal length, OID overlong base-128. Всегда reject: BOOLEAN length≠1 / constructed; empty INTEGER; truncated EOC; indefinite в DER. BER: indefinite, constructed строки/BIT STRING, время без секунд / `±hhmm`.

## Бенчмарки PKIX/CMS

[runtime-csharp/benchmarks/Asn1Kit.Pkix.Benchmarks](../runtime-csharp/benchmarks/Asn1Kit.Pkix.Benchmarks/) — BenchmarkDotNet Decode/Encode для `Certificate`, `CertificateList`, CMS `ContentInfo` (attached SignedData). Типы из [Asn1Kit.Pkix.Bench](../runtime-csharp/generated/Asn1Kit.Pkix.Bench/) (`Asn1Kit.Pkix.Bench` / `Asn1Kit.Cms.Bench`), собранного из golden `cms-2004.json` + [cms-2004-bench.patch.json](../compiler/fixtures/ir/cms-2004-bench.patch.json) (lazy на `CertificateChoices.certificate`; `retainEncoded` на TBS Name/SPKI/Extensions; `AttributeTypeAndValue` как `struct`). Encode Cert/CRL — hand-built object graph (не decode→encode). CMS-группы: Lazy / Lazy+Materialize / Eager (golden) / BCL ± materialize / BouncyCastle. Не в gate `dotnet test`.

```powershell
dotnet run -c Release --project runtime-csharp/benchmarks/Asn1Kit.Pkix.Benchmarks
```

## Backlog

### Открыто

1. XML-документация для public API Runtime
2. В сгенерированном коде — копия ASN.1-описания (и комментарии из модуля)
3. Open-type follow-up:
   - **3a.** остальные PKIX ANY (`AnotherName`, `ExtensionAttribute`, DN `AttributeValue`) через overlay
   - **3b.** curated `.asn` параметров алгоритмов из RFC 5912 (без `CLASS`) + bindings
   - **3c.** парсер/IR для `CLASS`, object sets, parameterized `AlgorithmIdentifier{…}`
4. Опция сохранения исходного закодированного представления
5. Пул массивов (constructed BER concat, DER SET OF sort)
6. Второй oracle — BouncyCastle (не gate `dotnet test`)
7. Модуль **DVCS** (CMS уже: [cms-2004.asn](../compiler/fixtures/asn1/cms-2004.asn) → `Asn1Kit.Cms`; codec-кейсы — [pkix-test-backlog.md](pkix-test-backlog.md))

### Крупные

1. C++ backend и C++ runtime — [new-backend.md](../compiler/docs/playbooks/new-backend.md)
2. Инструменты PKI поверх сгенерированного
