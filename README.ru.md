# Asn1Kit

[English](README.md) | Русский

Набор инструментов для работы с ASN.1: компилятор модуля во внутреннее JSON-представление, генератор кода и runtime BER/DER.

Репозиторий разбит на каталоги:

| Каталог | Содержание |
| --- | --- |
| [compiler/](compiler/) | IR, компилятор, codegen (C#), CLI и их тесты |
| [runtime-csharp/](runtime-csharp/) | C# runtime BER/DER и его тесты |
| [runtime-cpp/](runtime-cpp/) | C++20 BER/DER runtime (примитивы + SEQUENCE/SET scopes) |

## Компоненты

1. **Компилятор** — текст ASN.1 → JSON IR (`irVersion` + схема [schemas/asn1kit-ir-v1.json](schemas/asn1kit-ir-v1.json)). Файл можно править: `options.csharp.namespace`, имена типов, `generate: false`.
2. **Генератор** — IR → исходный код. Сейчас C#, позже C++.
3. **Runtime** — примитивы BER и DER на языке цели. Сгенерированный код вызывает эту библиотеку.

## Начало работы

Asn1Kit использует двухэтапный процесс: один или несколько ASN.1-модулей сначала компилируются в JSON IR, затем из IR генерируются исходные файлы C#. Отдельный IR можно проверить, изменить в нём параметры генерации или взять за основу один из готовых IR-файлов этого репозитория.

### 1. Создайте или выберите IR-файл

Чтобы скомпилировать собственный ASN.1-модуль, запустите CLI из корня репозитория. Параметр `-O csharp.namespace=...` задаёт пространство имён генерируемых типов:

```powershell
dotnet run --project compiler/src/Asn1Kit.Cli -- compile `
  -i path/to/MyProtocol.asn `
  -o artifacts/MyProtocol.json `
  -O csharp.namespace=MyCompany.MyProtocol
```

Если модуль импортирует определения из других ASN.1-модулей, передайте несколько файлов через `-i`.

Вместо компиляции ASN.1 можно взять готовый IR-файл из [compiler/fixtures/ir](compiler/fixtures/ir/). Скопируйте его в свой проект, если хотите настроить `options`: пространство имён C#, имена генерируемых элементов или исключение отдельных определений из генерации. Формат IR описан в [docs/ir-schema.md](docs/ir-schema.md).

### 2. Сгенерируйте исходные файлы C#

Создайте библиотеку классов для модуля и сгенерируйте в неё файлы `.g.cs`:

```powershell
dotnet new classlib --framework net6.0 -o generated/MyProtocol

dotnet run --project compiler/src/Asn1Kit.Cli -- generate `
  -i artifacts/MyProtocol.json `
  --lang csharp `
  -o generated/MyProtocol
```

Для сокращённого сценария в `generate` можно передать `.asn` напрямую. Отдельный IR удобнее, если его параметры требуется проверять или поддерживать вручную.

### 3. Подключите runtime

Сгенерированные исходные файлы C# вызывают `Asn1Writer`, `Asn1Reader` и другие API из `Asn1Kit.Runtime`, поэтому библиотеке со сгенерированным кодом необходима ссылка на runtime. При работе из этого репозитория добавьте ссылку на проект:

```powershell
dotnet add generated/MyProtocol/MyProtocol.csproj reference `
  runtime-csharp/src/Asn1Kit.Runtime/Asn1Kit.Runtime.csproj
```

После публикации `Asn1Kit.Runtime` в NuGet вместо этого можно будет подключить пакет:

```powershell
dotnet add generated/MyProtocol/MyProtocol.csproj package Asn1Kit.Runtime
```

Затем добавьте ссылку на сгенерированную библиотеку в своё приложение:

```powershell
dotnet add path/to/MyApplication.csproj reference `
  generated/MyProtocol/MyProtocol.csproj
```

У сгенерированных составных типов есть экземплярный метод `Encode(Asn1Writer)` и статический метод `Decode(Asn1Reader)`. Типичный цикл кодирования и декодирования DER выглядит так:

```csharp
using Asn1Kit.Runtime;
using MyCompany.MyProtocol;

var value = new MyMessage
{
    // Инициализируйте поля, созданные из ASN.1-определения.
};

var writer = new Asn1Writer(Asn1Encoding.Der);
value.Encode(writer);
byte[] encoded = writer.Encode();

var reader = new Asn1Reader(encoded, Asn1Encoding.Der);
MyMessage decoded = MyMessage.Decode(reader);
```

Замените `MyMessage` типом из своего ASN.1-модуля. Перед использованием конструкций за пределами поддержанного профиля компилятора и runtime сверьтесь с текущей [матрицей поддержки](docs/status.md).

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

`generate` также принимает `.asn` напрямую: компиляция выполняется в памяти. Повторяемый `-O` / `--option path=value` задаёт `options` всем модулям (например `-O csharp.namespace=Asn1Kit.Pkix` для golden PKIX).

Эталонный C# PKIX/CMS/DVCS и зависимых протоколов: [runtime-csharp/generated/Asn1Kit.Pkix](runtime-csharp/generated/Asn1Kit.Pkix/).

## Профиль компилятора

Модули с `DEFINITIONS`, теги `EXPLICIT` / `IMPLICIT` / `AUTOMATIC`, `IMPORTS`/`EXPORTS` внутри переданных файлов.

Типы: `BOOLEAN`, `INTEGER`, `ENUMERATED`, `BIT STRING`, `OCTET STRING`, `NULL`, `OBJECT IDENTIFIER`, строковые типы (`UTF8String`, `PrintableString`, `IA5String`, …), `UTCTime` / `GeneralizedTime`, `SEQUENCE` / `SEQUENCE OF`, `SET` / `SET OF`, `CHOICE`, `ANY` / `ANY DEFINED BY`, `OPTIONAL`, `DEFAULT`, constraints `SIZE` / диапазоны (ссылки на `ub-*` разрешаются).

Value assignments (`id-pkix OBJECT IDENTIFIER ::= { … }`, `ub-name INTEGER ::= 32768`) попадают в `module.values`.

Опорные фикстуры RFC 5280: [compiler/fixtures/asn1/pkix1-explicit88.asn](compiler/fixtures/asn1/pkix1-explicit88.asn) (Appendix A.1) и [compiler/fixtures/asn1/pkix1-implicit88.asn](compiler/fixtures/asn1/pkix1-implicit88.asn) (Appendix A.2, с `IMPORTS` из Explicit88).

Полный граф DVCS основан на ASN.1:1988 из RFC 3029 и исходных CMP/CRMF/OCSP/ESS/S/MIME RFC; устаревшие X.509/CMS imports нормализованы на локальные RFC 5280 и `CryptographicMessageSyntax2004`. Публичные namespace: `Asn1Kit.Dvcs`, `.Cmp`, `.Crmf`, `.Ocsp`, `.Ess`, `.Smime`, `.Pkcs10`.

Два профиля на одном IR и C# backend:

- **Legacy** (PKCS/PKIX Explicit88/Implicit88, curated CMS 2004, граф DVCS) — ASN.1:1988 с `ANY` / overlay bindings; в этих модулях нет `CLASS` и параметризации.
- **Modern** — корпус RFC 5911/5912/6268/8410 с `CLASS`, objects/sets, `WITH SYNTAX` и параметризованными типами; выпуск — [Asn1Kit.Modern](runtime-csharp/generated/Asn1Kit.Modern/).

Всегда явный отказ: `COMPONENTS OF`, `REAL`, `EXTERNAL` и формы IOC/параметризации вне документированного modern-профиля. Детали и пробелы — [docs/status.md](docs/status.md).

C# backend генерирует `sequence` / `choice` / `sequenceOf` / `set` / `setOf`, `enumerated` → C# `enum`, примитивы (`boolean`, `integer`, `octetString`, `oid`, `bitString`, `string`, `time`, `any` → `Asn1Any`).

Кодировки runtime: BER (в том числе indefinite length на чтении) и DER (каноническая запись).

Подробности: [docs/architecture.md](docs/architecture.md), [docs/ir-schema.md](docs/ir-schema.md), текущее состояние поддержки — [docs/status.md](docs/status.md).

## Лицензия

Asn1Kit распространяется по лицензии [Apache License 2.0](LICENSE). Она разрешает коммерческое использование, изменение и распространение, в том числе в составе закрытых продуктов, при соблюдении условий лицензии.

Код, созданный генератором Asn1Kit из пользовательских ASN.1-модулей или IR, можно использовать без дополнительных ограничений со стороны Asn1Kit. На сторонние материалы, для которых в репозитории явно указан источник или отдельная лицензия, распространяются условия соответствующего правообладателя.
