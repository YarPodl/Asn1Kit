# Архитектура Asn1Kit

Три независимых слоя. Компилятор знает ASN.1, генератор знает целевой язык, runtime знает BER/DER.

```text
ASN.1 module  -->  Compiler (C#)  -->  IR JSON (schema v1)
                                           |
                                           v  (можно править options)
                                      Code generator
                                           |
                                           v
                                   Generated C# types
                                           |
                                           v
                                   Asn1Kit.Runtime (BER/DER)
```

## Каталоги репозитория

| Каталог | Роль |
| --- | --- |
| [compiler/](../compiler/) | IR, компилятор, codegen, CLI, ASN.1/IR-фикстуры и тесты инструментов |
| [runtime-csharp/](../runtime-csharp/) | C# BER/DER runtime, hex-фикстуры и тесты кодека |
| [runtime-cpp/](../runtime-cpp/) | Заглушка под будущий C++ runtime |
| [schemas/](../schemas/) | Общий контракт IR (JSON Schema) |
| [docs/](.) | Сквозная документация (этот файл, status, decisions, ir-schema) |

Один корневой `Asn1Kit.sln` собирает оба рабочих каталога.

## Проекты

| Проект | Каталог | Роль |
| --- | --- | --- |
| `Asn1Kit.Ir` | compiler | Модель IR, JSON, валидация по JSON Schema |
| `Asn1Kit.Compiler` | compiler | Лексер, парсер AST, семантический resolver, построение IR |
| `Asn1Kit.Codegen` | compiler | `ILanguageBackend` |
| `Asn1Kit.Codegen.CSharp` | compiler | Генерация `.cs` |
| `Asn1Kit.Cli` | compiler | Команды `compile` и `generate` |
| `Asn1Kit.Runtime` | runtime-csharp | TLV, примитивы BER/DER |
| `Asn1Kit.Modern` | runtime-csharp/generated | Отдельная сборка PKIX/CMS RFC 5911/5912/6268/8410 |

C++ планируется как backend в `compiler/` и runtime в `runtime-cpp/`, без изменений фронтенда.

## IR

Единственный контракт — JSON со схемой `schemas/asn1kit-ir-v1.json`. Поле `irVersion`. Словари `options` расширяемы. Генератор читает `options.csharp.*` и `options.generate`.

`asn1kit compile` перезаписывает IR из ASN.1. Правки `options` вносите в JSON перед `generate`, либо не пересобирайте IR. Альтернатива — sidecar options-patch (`IrOptionsPatch` / CLI `--patch`): deep-merge в `module.Options` и в `IrComponent.Options` у SEQUENCE/SET/CHOICE (`Module.Type.field`), а также в `types` (`Module.Type`). На `generate` можно записать результат через `--ir-output`. Продуктовые options PKIX/CMS/DVCS задаёт [dvcs.patch.json](../compiler/fixtures/ir/dvcs.patch.json).

Компилятор проходит четыре этапа: `Asn1Parser` создаёт AST; `InformationResolver` собирает символы всех модулей, разрешает imports/governors/IOC, специализирует параметризованные типы и превращает object sets в таблицы; `IrBuilder` строит конкретный IR, разворачивает OID-цепочки, bounds и DEFAULT; `SpecializationCompactor` объединяет типы, различающиеся лишь IOC-таблицами, и переносит таблицы на ссылки. После этого выполняются `IrValidator` и проверка JSON Schema. Частные CLASS и шаблоны в публичный IR не попадают. Ссылки вперёд разрешаются после сбора объявлений, генератор не выполняет ASN.1-семантику.

### Границы профиля компилятора

Поддерживаются legacy PKCS/PKIX/DVCS и современный корпус PKIX/CMS с CLASS, objects/sets, WITH SYNTAX и параметризованными типами. Проверяемые конструкции и ограничения перечислены в [status.md](status.md). Вне профиля — явная `CompileException` с позицией; нераспознанные constraint-формы сохраняются в `constraint.unsupported`.

C# codegen пока не покрывает все kind IR; на неподдерживаемых kind бросает `NotSupportedException`.

## Runtime

`Asn1Writer` / `Asn1Reader` принимают `Asn1Encoding.Ber` или `Asn1Encoding.Der`.
Публичный API Writer/Reader — [runtime-csharp/docs/runtime-api.md](../runtime-csharp/docs/runtime-api.md).

- DER: только definite length, BOOLEAN `0xFF` / `0x00`; INTEGER на записи — минимальная signed big-endian форма.
- BER: decode принимает definite и indefinite length; constructed `OCTET STRING` склеивается.

Сгенерированные типы вызывают `Write*` / `Read*` напрямую и не дублируют кодек.
