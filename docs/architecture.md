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

## Проекты

| Проект | Роль |
| --- | --- |
| `Asn1Kit.Ir` | Модель IR, JSON, валидация по JSON Schema |
| `Asn1Kit.Compiler` | Лексер, парсер, резолв имён, эмит IR |
| `Asn1Kit.Codegen` | `ILanguageBackend` |
| `Asn1Kit.Codegen.CSharp` | Генерация `.cs` |
| `Asn1Kit.Runtime` | TLV, примитивы BER/DER |
| `Asn1Kit.Cli` | Команды `compile` и `generate` |

C++ планируется как ещё один backend и отдельный runtime, без изменений фронтенда.

## IR

Единственный контракт — JSON со схемой `schemas/asn1kit-ir-v1.json`. Поле `irVersion`. Словари `options` расширяемы. Генератор читает `options.csharp.*` и `options.generate`.

`asn1kit compile` перезаписывает IR из ASN.1. Правки `options` вносите в JSON перед `generate`, либо не пересобирайте IR.

Компилятор делает двухфазный резолв значений: сначала собирает все type/value assignments модулей, затем разворачивает OID-цепочки, подставляет `ub-*` в constraints и `DEFAULT`. Это нужно, потому что в реальных модулях (например PKIX1Explicit88) upper bounds объявлены в конце файла.

### Границы профиля компилятора

Поддерживается широко используемое подмножество X.680 / PKCS-модулей: value assignments, `SET`/`SET OF`, `BIT STRING`, строки, время, `ANY DEFINED BY`, `DEFAULT`, `SIZE`/диапазоны. Вне профиля — явная `CompileException`: `CLASS` / information objects, `COMPONENTS OF`, параметризованные типы, `REAL`, `EXTERNAL`. Нераспознанные constraint-формы сохраняются в `constraint.unsupported`, а не отбрасываются молча.

C# codegen пока не покрывает все kind IR; на неподдерживаемых kind бросает `NotSupportedException`.

## Runtime

`Asn1Writer` / `Asn1Reader` принимают `Asn1Encoding.Ber` или `Asn1Encoding.Der`.
Ревью API и кандидаты на правки до тестов — [runtime-api.md](runtime-api.md).

- DER: только definite length, BOOLEAN `0xFF` / `0x00`; INTEGER на записи через `BigInteger.ToByteArray`.
- BER: decode принимает definite и indefinite length; constructed `OCTET STRING` склеивается.

Сгенерированные типы вызывают `Write*` / `Read*` напрямую и не дублируют кодек.
