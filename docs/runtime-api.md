# Ревью публичного API Asn1Kit.Runtime

Цель документа: понять, **что в API стоит поменять до начала полной матрицы тестов**, чтобы потом дешевле оптимизировать реализацию. Смена API позже не запрещена — только дороже (переписывание тестов и codegen).

Реализация — `[src/Asn1Kit.Runtime](../src/Asn1Kit.Runtime)`. Как править кодек — [playbooks/runtime.md](playbooks/runtime.md).
Что уже поддержано по типам — [status.md](status.md) § Runtime BER/DER.

## Порядок работ

```text
1. Этот обзор → решение по правкам API (если нужны)
2. Полная матрица RuntimeTests по чеклисту ниже (уже по выбранным сигнатурам)
3. Внутренние оптимизации по backlog
```



## Кто кого вызывает

Горячий путь — прямые `Write*` / `Read*` из C# backend, не обёртки `Asn1Primitives`:

```text
CSharpBackend → Asn1Writer.Write* / Asn1Reader.Read*
              → Asn1Tag, Asn1BitString, Asn1Any, Asn1Exception
```

Статические `Asn1Boolean` / `Asn1Integer` / … — convenience для тестов и прикладного кода. Codegen их не эмитит.

Непублично: `Asn1TextCodec` (`internal`), `Asn1Writer.EncodeInteger` (`internal`), `WriteTag` / `WriteLength` / `WriteTlv` / `WritePrimitive` (`private`).

## Инвентарь

Роли: **hot** — каждый generated encode/decode; **warm** — тесты / helpers / приложения; **cold** — escape hatch, codegen не использует.

Ownership: **owned** — вызывающий получает или отдаёт владение копией; **borrow** — данные живут у writer/reader или во входном буфере на время вызова.

Колонка «без смены API»: можно ли убрать главные аллокации, оставив текущие сигнатуры.


| Символ                                                                                                                   | Роль             | Ownership               | Без смены API                                                |
| ------------------------------------------------------------------------------------------------------------------------ | ---------------- | ----------------------- | ------------------------------------------------------------ |
| `Asn1Encoding`, `Asn1TagClass`, `Asn1StringForm`, `Asn1TimeForm`                                                         | hot              | —                       | да                                                           |
| `Asn1Tag` (ctor, props, `AsConstructed`/`AsPrimitive`, universal statics, `Equals` / `MatchesIgnoreConstructed`)         | hot              | value                   | да                                                           |
| `Asn1Exception`                                                                                                          | hot              | —                       | да                                                           |
| `Asn1Writer(Asn1Encoding)` / `Encoding`                                                                                  | hot              | —                       | да (другой backing buffer)                                   |
| `Asn1Writer.Encode() → byte[]`                                                                                           | hot              | owned snapshot          | частично (меньше копий до выхода; return остаётся `byte[]`)  |
| `WriteBoolean` / `WriteInteger` / `WriteNull` / `WriteObjectIdentifier` / `WriteBitString` / `WriteString` / `WriteTime` | hot              | вход по значению / Span | да (scratch)                                                 |
| `WriteOctetString(ReadOnlySpan<byte>)`                                                                                   | hot              | borrow                  | да (убрать внутренний `ToArray`)                             |
| `WriteSequence` / `WriteSet` / `WriteExplicit(Action<Asn1Writer>)`                                                       | hot              | callback                | **да — главный рычаг записи**                                |
| `WriteSetOf(IReadOnlyList<byte[]>)`                                                                                      | hot (SET OF)     | owned TLV               | частично; форма требует готовых массивов                     |
| `WriteAny` / `WriteAny(tag, …)`                                                                                          | hot              | owned в `Asn1Any`       | да                                                           |
| `WriteRaw(ReadOnlySpan<byte>)`                                                                                           | cold             | borrow                  | да                                                           |
| `Asn1Reader(byte[])` / `Encoding` / `Eof` / `TryPeekTag`                                                                 | hot              | buffer у reader         | да для тел; ctor только `byte[]` ограничивает вход           |
| `ReadSequence` / `ReadSet` (`Action` / `Func`)                                                                           | hot              | —                       | **да — главный рычаг чтения** (private offset-ctor уже есть) |
| `ReadBoolean`…`ReadTime`                                                                                                 | hot              | owned где применимо     | да                                                           |
| `ReadOctetString → byte[]`                                                                                               | hot              | owned                   | частично                                                     |
| `ReadAny` / `ReadAny(expected)`                                                                                          | hot              | owned                   | да                                                           |
| `ReadValue → byte[]`                                                                                                     | cold/warm        | owned                   | hot path не обязан идти через него                           |
| `ReadTlv → (Tag, byte[] Contents, Constructed)`                                                                          | cold/warm        | owned Contents          | всегда копия наружу при этой сигнатуре                       |
| `Asn1BitString` / `Asn1Any`                                                                                              | hot              | owned                   | да внутри encode/decode                                      |
| `Asn1Boolean`…`Asn1Time` wrappers                                                                                        | warm             | делегируют              | да                                                           |
| `Asn1ObjectIdentifier.ParseArcs` / `EncodeContents`                                                                      | warm / hot-write | owned                   | да                                                           |




### Граница владения при текущих сигнатурах

Пока return-тип `byte[]` / owned struct — аллокация на выходе неизбежна:

- `Encode() → byte[]`
- `ReadOctetString` / `ReadValue` / публичный `ReadTlv` → `byte[]`
- `Asn1Any.Contents`, ctor `Asn1BitString` / `Asn1Any`
- OID: `string` ↔ `ParseArcs` / `EncodeContents`
- `WriteSetOf(IReadOnlyList<byte[]>)`

Менять эти типы имеет смысл только если нужен zero-copy наружу; иначе достаточно внутренней оптимизации.

## Кандидаты на смену API до тестов

Имеет смысл решить **сейчас** (дешевле, чем после полной матрицы). Не обязательно менять всё — список для выбора.


| Кандидат                                                                 | Зачем                                                                                 | Стоимость, если отложить                |
| ------------------------------------------------------------------------ | ------------------------------------------------------------------------------------- | --------------------------------------- |
| Форма `WriteSetOf` + codegen (сейчас `List<byte[]>` + writer на элемент) | убрать обязательные промежуточные TLV-массивы; callback/`Action` как у SEQUENCE       | переписать SET OF тесты и эмит backend  |
| Публичность `ReadTlv` / `ReadValue`                                      | hot path и так не использует; можно сузить surface или дать Span/offset-вариант рядом | тесты escape-hatch и любой внешний код  |
| Вход reader: только `byte[]`                                             | `ReadOnlyMemory<byte>` / `(byte[], offset, length)` public ctor                       | все тесты, создающие reader             |
| `Encode() → byte[]` only                                                 | additive `TryEncode(Span)` / запись в caller buffer                                   | тесты snapshot-encode                   |
| `ReadOctetString → byte[]` only                                          | Span/`ReadOnlyMemory` перегрузка или «copy out» явно                                  | тесты OCTET и codegen property `byte[]` |
| Wrappers `Asn1Primitives`                                                | оставить как warm API или свести к тестам; на hot path не влияют                      | низкая                                  |


Остальное (SEQUENCE callback, примитивы `Write*`/`Read*`, `Asn1Tag`) уже удобно для внутренней оптимизации — менять не обязательно ради perf.

## Backlog внутренней оптимизации (сигнатуры можно не трогать)

1. **Nested write:** один буфер / резерв длины вместо `new Asn1Writer` + `Encode()` на уровень.
2. **Nested read:** срез `(offset, end)` в исходном `_data`; публичный `ReadTlv` может остаться allocating-обёрткой.
3. **Constructed OCTET / BIT / string (BER):** без `List<byte>` + `AddRange` / лишних `ToArray`.
4. **OID encode:** без `Split` + `List` + `Stack` на коротких OID.
5. **Scratch:** BOOLEAN `new byte[]{…}`, `WriteOctetString` → `ToArray`, high-tag / length scratch, лишний reverse в `ReadInteger`.



## Заметки по реализации (не планы ужесточения)

- `definiteOnly` в private `WriteTlv` игнорируется; indefinite на записи не эмитится — так и есть.
- Docs раньше путали wrappers и codegen — фактический hot path: `Write*` / `Read*`.



## Чеклист RuntimeTests

Писать в `RuntimeTests` ([tests/Asn1Kit.Tests/Asn1KitTests.cs](../tests/Asn1Kit.Tests/Asn1KitTests.cs)) на байтовых векторах, не только через generated types. Round-trip через Roslyn — дополнение, не замена.

На каждый примитив / операцию из таблицы status § Runtime:

1. **DER encode** — ожидаемые байты.
2. **DER decode** тех же байт.
3. **BER decode** известного вектора (где применимо: indefinite length, constructed OCTET/BIT/string).
4. **Round-trip** encode → decode → encode совпадает побайтово (DER).
5. **Границы:** пустое значение, `0`, `-1`, длинный `BigInteger`, длина > 127 (long-form length), вложенность SEQUENCE.
6. **Отказы (уже в коде):** чужой тег, обрезанный TLV, EOF; DER indefinite length; BOOLEAN не `0x00`/`0xFF`; BIT STRING с ненулевыми trailing bits в DER.

Покрытие по API:


| API                                              | Минимум                                                                   |
| ------------------------------------------------ | ------------------------------------------------------------------------- |
| `WriteBoolean` / `ReadBoolean`                   | DER `00`/`FF`; BER nonzero-as-true                                        |
| `WriteInteger` / `ReadInteger`                   | `0`, `-1`, 127/128, -128, длинный; empty reject                           |
| `WriteOctetString` / `ReadOctetString`           | empty; long-form; BER constructed + indefinite                            |
| `WriteNull` / `ReadNull`                         | empty OK; nonempty reject                                                 |
| `WriteObjectIdentifier` / `ReadObjectIdentifier` | типичный OID; слишком короткий; truncated; `ParseArcs` / `EncodeContents` |
| `WriteBitString` / `ReadBitString`               | unusedBits 0..7; DER trailing-zero; BER constructed                       |
| `WriteString` / `ReadString`                     | все 12 `Asn1StringForm` (хотя бы smoke); BER constructed UTF8             |
| `WriteTime` / `ReadTime`                         | UTC + Generalized; fractionDigits; BER offset / без секунд                |
| `WriteSequence` / `ReadSequence`                 | вложенность; OPTIONAL via `TryPeekTag`/`Eof`                              |
| `WriteSet` / `ReadSet`                           | tag SET; decode по тегу                                                   |
| `WriteSetOf`                                     | DER lexicographic sort; BER порядок не валидируется                       |
| `WriteExplicit`                                  | как constructed wrapper                                                   |
| `WriteAny` / `ReadAny`                           | без тега / с IMPLICIT; BER indefinite                                     |
| `WriteRaw`                                       | append готового TLV (хотя бы один вектор)                                 |
| `ReadValue` / `ReadTlv`                          | owned contents; wrong tag; truncated                                      |
| Wrappers `Asn1Boolean`…`Asn1Time`                | делегирование (smoke encode/decode)                                       |
| `Asn1BitString`                                  | `FromBits`, indexer, equality                                             |
| `Asn1Any`                                        | equality; IMPLICIT encode                                                 |


