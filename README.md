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

## Профиль компилятора

Модули с `DEFINITIONS`, теги `EXPLICIT` / `IMPLICIT` / `AUTOMATIC`, `IMPORTS`/`EXPORTS` внутри переданных файлов.

Типы: `BOOLEAN`, `INTEGER`, `ENUMERATED`, `BIT STRING`, `OCTET STRING`, `NULL`, `OBJECT IDENTIFIER`, строковые типы (`UTF8String`, `PrintableString`, `IA5String`, …), `UTCTime` / `GeneralizedTime`, `SEQUENCE` / `SEQUENCE OF`, `SET` / `SET OF`, `CHOICE`, `ANY` / `ANY DEFINED BY`, `OPTIONAL`, `DEFAULT`, constraints `SIZE` / диапазоны (ссылки на `ub-*` разрешаются).

Value assignments (`id-pkix OBJECT IDENTIFIER ::= { … }`, `ub-name INTEGER ::= 32768`) попадают в `module.values`.

Опорная фикстура: [fixtures/asn1/pkix1-explicit88.asn](fixtures/asn1/pkix1-explicit88.asn) (RFC 5280 Appendix A.1).

Вне профиля (явная ошибка): information object classes (`CLASS`), `COMPONENTS OF`, параметризованные типы, `REAL`, `EXTERNAL`.

C# backend пока генерирует прежний набор (`sequence` / `choice` / `sequenceOf` / примитивы); новые kind (`set`, `setOf`, `any`, `string`, `time`, `bitString`) отклоняются с понятным сообщением.

Кодировки runtime: BER (в том числе indefinite length на чтении) и DER (каноническая запись).

Подробности: [docs/architecture.md](docs/architecture.md), [docs/ir-schema.md](docs/ir-schema.md), текущее состояние поддержки — [docs/status.md](docs/status.md).
