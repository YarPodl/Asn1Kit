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
| `Asn1Kit.Compiler` | compiler | Лексер, парсер, резолв имён, эмит IR |
| `Asn1Kit.Codegen` | compiler | `ILanguageBackend` |
| `Asn1Kit.Codegen.CSharp` | compiler | Генерация `.cs` |
| `Asn1Kit.Cli` | compiler | Команды `compile` и `generate` |
| `Asn1Kit.Runtime` | runtime-csharp | TLV, примитивы BER/DER |

C++ планируется как backend в `compiler/` и runtime в `runtime-cpp/`, без изменений фронтенда.

## IR

Единственный контракт — JSON со схемой `schemas/asn1kit-ir-v1.json`. Поле `irVersion`. Словари `options` расширяемы. Генератор читает `options.csharp.*` и `options.generate`.

`asn1kit compile` перезаписывает IR из ASN.1. Правки `options` вносите в JSON перед `generate`, либо не пересобирайте IR.

Компилятор делает двухфазный резолв значений: сначала собирает все type/value assignments модулей, затем разворачивает OID-цепочки, подставляет `ub-*` в constraints и `DEFAULT`. Это нужно, потому что в реальных модулях (например PKIX1Explicit88) upper bounds объявлены в конце файла.

### Границы профиля компилятора

Поддерживается широко используемое подмножество X.680 / PKCS-модулей: value assignments, `SET`/`SET OF`, `BIT STRING`, строки, время, `ANY DEFINED BY`, `DEFAULT`, `SIZE`/диапазоны. Вне профиля — явная `CompileException`: `CLASS` / information objects, `COMPONENTS OF`, параметризованные типы, `REAL`, `EXTERNAL`. Нераспознанные constraint-формы сохраняются в `constraint.unsupported`, а не отбрасываются молча.

C# codegen пока не покрывает все kind IR; на неподдерживаемых kind бросает `NotSupportedException`.

## Runtime

`Asn1Writer` / `Asn1Reader` принимают `Asn1Encoding.Ber` или `Asn1Encoding.Der`.
Публичный API Writer/Reader — [runtime-csharp/docs/runtime-api.md](../runtime-csharp/docs/runtime-api.md).

- DER: только definite length, BOOLEAN `0xFF` / `0x00`; INTEGER на записи — минимальная signed big-endian форма.
- BER: decode принимает definite и indefinite length; constructed `OCTET STRING` склеивается.

Сгенерированные типы вызывают `Write*` / `Read*` напрямую и не дублируют кодек.
