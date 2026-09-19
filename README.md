# Asn1Kit

Набор инструментов для работы с ASN.1: компилятор модуля во внутреннее JSON-представление, генератор кода и runtime BER/DER.

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
dotnet run --project src/Asn1Kit.Cli -- compile -i fixtures/asn1/example.asn -o fixtures/ir/example.json
dotnet run --project src/Asn1Kit.Cli -- generate -i fixtures/ir/example.json --lang csharp -o ./generated
```

`generate` также принимает `.asn` напрямую: компиляция выполняется в памяти.

## Профиль v0

Модули с `DEFINITIONS`, теги `EXPLICIT` / `IMPLICIT` / `AUTOMATIC`, `IMPORTS` внутри переданных файлов.

Типы: `BOOLEAN`, `INTEGER`, `ENUMERATED`, `OCTET STRING`, `NULL`, `OBJECT IDENTIFIER`, `SEQUENCE`, `SEQUENCE OF`, `CHOICE`, `OPTIONAL`.

Кодировки runtime: BER (в том числе indefinite length на чтении) и DER (каноническая запись).

Подробности: [docs/architecture.md](docs/architecture.md), [docs/ir-schema.md](docs/ir-schema.md).
