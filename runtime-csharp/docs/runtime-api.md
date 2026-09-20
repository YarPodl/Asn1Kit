# Публичный API Asn1Kit.Runtime

Контракт Writer/Reader: ownership буферов, горячий путь codegen и инвентарь символов.

Реализация — [`src/Asn1Kit.Runtime`](../src/Asn1Kit.Runtime). Как править кодек — [playbooks/runtime.md](playbooks/runtime.md).
Что уже поддержано по типам — [status.md](../../docs/status.md) § Runtime BER/DER.
Backlog оптимизаций — там же, § «Backlog: оптимизация runtime».

## Политика Memory / Span

- **Вход** и **буфер вызывающего** — `ReadOnlySpan` / `Span` (у `Memory` вызывайте `.Span`; парные перегрузки Span+Memory не делаем — `byte[]` неоднозначен).
- **Вход reader** — дополнительно `ReadOnlyMemory<byte>` ctor (без копии, если array-backed).
- **Значения, уходящие из reader** — **owned `byte[]`** / owned structs; `ContentsMemory` / `Asn1BitString.Memory` — view на owned массив, не на буфер reader.

## Кто кого вызывает

Горячий путь — прямые `Write*` / `Read*` из C# backend, не обёртки `Asn1Primitives`:

```text
CSharpBackend → Asn1Writer.Write* / Asn1Reader.Read*
              → Asn1Tag, Asn1BitString, Asn1Any, Asn1Exception
```

Статические `Asn1Boolean` / `Asn1Enumerated` / … — warm convenience; codegen их не эмитит.
`Asn1Integer` — **hot** value type (owned DER contents) для codegen при `representation=der`; статические `Encode(BigInteger)` / `DecodeBigInteger` — warm.

Непублично: `Asn1TextCodec` (`internal`), `Asn1Writer.EncodeInteger` (`internal`), `WriteTag` / `WriteLength` / `WriteTlv` / `WritePrimitive` (`private`).

## Инвентарь

Роли: **hot** — каждый generated encode/decode; **warm** — тесты / helpers; **cold** — escape hatch.

| Символ | Роль | Ownership |
| --- | --- | --- |
| `Asn1Writer.EncodedLength` / `TryEncode(Span)` / `Encode() → byte[]` | hot | snapshot / copy-out |
| `WriteOctetString(ReadOnlySpan)` / `WriteRaw(ReadOnlySpan)` | hot/cold | borrow |
| `WriteSequence` / `WriteSet` / `WriteSetOf` / `WriteSequenceOf<T>` / `WriteSetOf<T>` / `WriteExplicit(Action)` | hot | callback |
| `ReadSequenceOf<T>` / `ReadSetOf<T>` | hot | owned `List<T>` |
| `Asn1Reader(byte[]\|offset/length\|ReadOnlyMemory)` | hot | срез без копии на входе |
| `ReadOctetString → byte[]` / `TryReadOctetString(Span)` | hot | owned / copy-out (Try всегда продвигает reader) |
| `ReadValue → byte[]` / `TryReadValue(Span)` / `ReadTlv` | cold/warm | owned / copy-out |
| `Asn1Any.Contents` / `ContentsMemory` | hot | owned |
| `Asn1BitString.Span` / `Memory` | hot | owned |
| `Asn1Integer.Span` / `Memory` | hot | owned DER contents |
| `Asn1Primitives` wrappers (+ `Asn1OctetString.TryDecode`) | warm | делегируют |

## Заметки

- `definiteOnly` в private `WriteTlv` игнорируется; indefinite на записи не эмитится.
- `TryReadOctetString` / `TryReadValue` при нехватке destination возвращают `false`, но TLV уже потреблён.

## Soft-read и строгие опции

Политика: [docs/decisions.md](../../docs/decisions.md) «мягкое чтение»; инвентарь soft-accept — [docs/status.md](../../docs/status.md) § Runtime.

- **Encode** всегда канонический (минимальный INTEGER, нулевые trailing bits BIT STRING, …).
- **Decode** по умолчанию принимает зафиксированные неканоничные формы; строгий reject — опциями reader’а (план/backlog; API опций ещё не введён).
- Soft по умолчанию (reject выключен): non-minimal INTEGER contents; BIT STRING nonzero trailing bits (включая режим DER).
- Новый soft-accept без строки в status + этой секции — не допускается.
- В фикстурах: default-кейс = `encode: false` + успешный decode; strict-кейс = `reject: true` при включённой опции (когда опции появятся).

## Чеклист RuntimeTests

Писать в `PrimitiveCodecTests` / `PrimitiveOracleTests` / `ExternalVectorTests` (и точечно в `RuntimeTests`) на байтовых векторах из [fixtures/ber-der/](../fixtures/ber-der/). Round-trip через Roslyn — дополнение, не замена.

На каждый примитив из status § Runtime: DER encode/decode, BER где применимо, round-trip, границы, отказы (чужой тег, truncated, DER indefinite, BOOLEAN не `00`/`FF`). Для soft-форм — пара default-accept / strict-reject.

| API | Минимум |
| --- | --- |
| `TryEncode` / `EncodedLength` | exact fit; short Span → false; равенство с `Encode()` |
| `TryReadOctetString` / `TryReadValue` | fit; short → false; BER constructed OCTET |
| `WriteBoolean` / `ReadBoolean` | DER `00`/`FF`; BER nonzero-as-true |
| `WriteInteger` / `ReadInteger` / `ReadIntegerValue` / `ReadInt32`… | `0`, `-1`, 127/128, длинный; empty reject; soft non-minimal accept + as-is write через `Asn1Integer`; fixed-width range reject |
| `WriteEnumerated` / `ReadEnumerated` | tag `0A`; contents как INTEGER; empty / wrong tag reject |
| `WriteOctetString` / `ReadOctetString` | empty; long-form; BER constructed + indefinite; ROM overload |
| `WriteNull` / `ReadNull` | empty OK; nonempty reject |
| `WriteObjectIdentifier` / `ReadObjectIdentifier` | OID; arcs; rejects |
| `WriteBitString` / `ReadBitString` | unusedBits; encode trailing-zero; soft nonzero trailing accept + strict reject; BER constructed |
| `WriteString` / `ReadString` | 12 forms smoke; BER constructed UTF8 |
| `WriteTime` / `ReadTime` | UTC + Generalized; fractionDigits; BER |
| `WriteSequence` / `ReadSequence` | вложенность; OPTIONAL |
| `WriteSet` / `ReadSet` | tag SET |
| `WriteSetOf` / `WriteSetOf<T>` | DER sort; BER order |
| `WriteSequenceOf<T>` / `ReadSequenceOf<T>` / `ReadSetOf<T>` | list round-trip; empty list |
| `WriteExplicit` | constructed wrapper |
| `WriteAny` / `ReadAny` | IMPLICIT; ContentsMemory |
| `WriteRaw` | append TLV |
| `ReadValue` / `ReadTlv` | owned; wrong tag |
| Wrappers | smoke |
| `Asn1BitString` / `Asn1Any` / `Asn1Integer` | Memory/ContentsMemory; equality; `Asn1Integer` numeric accessors |
