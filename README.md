# Asn1Kit

Набор инструментов для работы с ASN.1: компилятор модуля во внутреннее JSON-представление, генератор кода и runtime BER/DER.

Репозиторий разбит на каталоги:

| Каталог | Содержание |
| --- | --- |
| [compiler/](compiler/) | IR, компилятор, codegen (C#), CLI и их тесты |
| [runtime-csharp/](runtime-csharp/) | C# runtime BER/DER и его тесты |
| [runtime-cpp/](runtime-cpp/) | Заглушка под будущий C++ runtime |

## Компоненты

1. **Компилятор** — текст ASN.1 → JSON IR (`irVersion` + схема [schemas/asn1kit-ir-v1.json](schemas/asn1kit-ir-v1.json)). Файл можно править: `options.csharp.namespace`, имена типов, `generate: false`.
2. **Генератор** — IR → исходный код. Сейчас C#, позже C++.
3. **Runtime** — примитивы BER и DER на языке цели. Сгенерированный код вызывает эту библиотеку.

## Сборка

Требуется .NET 6 SDK.

```text
dotnet build Asn1Kit.sln
dotnet test Asn1Kit.sln
```

## CLI

```text
dotnet run --project compiler/src/Asn1Kit.Cli -- compile -i compiler/fixtures/asn1/example.asn -o out.json
dotnet run --project compiler/src/Asn1Kit.Cli -- generate -i compiler/fixtures/ir/example.json --lang csharp -o ./generated
```

`generate` также принимает `.asn` напрямую: компиляция выполняется в памяти. Опция `--csharp-namespace` задаёт `options.csharp.namespace` всем модулям (используется для golden PKIX).

Эталонный C# PKIX: [runtime-csharp/generated/Asn1Kit.Pkix](runtime-csharp/generated/Asn1Kit.Pkix/).

## Профиль компилятора

Модули с `DEFINITIONS`, теги `EXPLICIT` / `IMPLICIT` / `AUTOMATIC`, `IMPORTS`/`EXPORTS` внутри переданных файлов.

Типы: `BOOLEAN`, `INTEGER`, `ENUMERATED`, `BIT STRING`, `OCTET STRING`, `NULL`, `OBJECT IDENTIFIER`, строковые типы (`UTF8String`, `PrintableString`, `IA5String`, …), `UTCTime` / `GeneralizedTime`, `SEQUENCE` / `SEQUENCE OF`, `SET` / `SET OF`, `CHOICE`, `ANY` / `ANY DEFINED BY`, `OPTIONAL`, `DEFAULT`, constraints `SIZE` / диапазоны (ссылки на `ub-*` разрешаются).

Value assignments (`id-pkix OBJECT IDENTIFIER ::= { … }`, `ub-name INTEGER ::= 32768`) попадают в `module.values`.

Опорные фикстуры RFC 5280: [compiler/fixtures/asn1/pkix1-explicit88.asn](compiler/fixtures/asn1/pkix1-explicit88.asn) (Appendix A.1) и [compiler/fixtures/asn1/pkix1-implicit88.asn](compiler/fixtures/asn1/pkix1-implicit88.asn) (Appendix A.2, с `IMPORTS` из Explicit88).

Вне профиля (явная ошибка): information object classes (`CLASS`), `COMPONENTS OF`, параметризованные типы, `REAL`, `EXTERNAL`.

C# backend генерирует `sequence` / `choice` / `sequenceOf` / `set` / `setOf`, примитивы (`boolean`, `integer`, `enumerated`, `octetString`, `oid`, `bitString`, `string`, `time`, `any` → `Asn1Any`).

Кодировки runtime: BER (в том числе indefinite length на чтении) и DER (каноническая запись).

Подробности: [docs/architecture.md](docs/architecture.md), [docs/ir-schema.md](docs/ir-schema.md), текущее состояние поддержки — [docs/status.md](docs/status.md).
