# Ревью публичного API Asn1Kit.Runtime

Цель документа: понять, **что в API стоит поменять до начала полной матрицы тестов**, чтобы потом дешевле оптимизировать реализацию. Смена API позже не запрещена — только дороже (переписывание тестов и codegen).

Реализация — [`src/Asn1Kit.Runtime`](../src/Asn1Kit.Runtime). Как править кодек — [playbooks/runtime.md](playbooks/runtime.md).
Что уже поддержано по типам — [status.md](status.md) § Runtime BER/DER.

## Порядок работ

```text
1. Этот обзор → решение по правкам API (если нужны)
2. Полная матрица RuntimeTests по чеклисту ниже — `PrimitiveCodecTests` + `fixtures/ber-der/` + oracle `PrimitiveOracleTests`
3. Внутренние оптимизации по backlog
```

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

Статические `Asn1Boolean` / `Asn1Integer` / … — warm convenience; codegen их не эмитит.

Непублично: `Asn1TextCodec` (`internal`), `Asn1Writer.EncodeInteger` (`internal`), `WriteTag` / `WriteLength` / `WriteTlv` / `WritePrimitive` (`private`).

## Инвентарь

Роли: **hot** — каждый generated encode/decode; **warm** — тесты / helpers; **cold** — escape hatch.

| Символ | Роль | Ownership |
| --- | --- | --- |
| `Asn1Writer.EncodedLength` / `TryEncode(Span)` / `Encode() → byte[]` | hot | snapshot / copy-out |
| `WriteOctetString(ReadOnlySpan)` / `WriteRaw(ReadOnlySpan)` | hot/cold | borrow |
| `WriteSequence` / `WriteSet` / `WriteSetOf` / `WriteExplicit(Action)` | hot | callback |
| `Asn1Reader(byte[]\|offset/length\|ReadOnlyMemory)` | hot | срез без копии на входе |
| `ReadOctetString → byte[]` / `TryReadOctetString(Span)` | hot | owned / copy-out (Try всегда продвигает reader) |
| `ReadValue → byte[]` / `TryReadValue(Span)` / `ReadTlv` | cold/warm | owned / copy-out |
| `Asn1Any.Contents` / `ContentsMemory` | hot | owned |
| `Asn1BitString.Span` / `Memory` | hot | owned |
| `Asn1Primitives` wrappers (+ `Asn1OctetString.TryDecode`) | warm | делегируют |

## Кандидаты на смену API до тестов

| Кандидат | Статус |
| --- | --- |
| Форма `WriteSetOf` + codegen | **сделано:** `Action<Asn1Writer>` |
| Публичность `ReadTlv` / `ReadValue` | **отменено** — остаются public |
| Вход reader | **сделано:** `offset`/`length` + `ReadOnlyMemory` |
| `Encode() → byte[]` only | **сделано:** `EncodedLength` + `TryEncode` |
| `ReadOctetString → byte[]` only | **сделано:** `TryReadOctetString` (+ `TryReadValue`); `byte[]` оставлен |
| Wrappers `Asn1Primitives` | **оставлен** warm API |

## Backlog внутренней оптимизации

1. **Nested write:** один буфер / резерв длины вместо `new Asn1Writer` + `Encode()` на уровень.
2. **Nested read:** срез `(offset, end)` без `ReadValue`→copy; публичный `ReadTlv` остаётся allocating-обёрткой.
3. **Constructed OCTET / BIT / string (BER):** без `List<byte>` + `AddRange`.
4. **OID encode:** без `Split` + `List` + `Stack` на коротких OID.
5. **Scratch:** high-tag / length; reverse в `ReadInteger`; `TryRead*` без промежуточного `byte[]` когда destination достаточен.

## Заметки

- `definiteOnly` в private `WriteTlv` игнорируется; indefinite на записи не эмитится.
- `TryReadOctetString` / `TryReadValue` при нехватке destination возвращают `false`, но TLV уже потреблён.

## Чеклист RuntimeTests

Писать в `PrimitiveCodecTests` / `PrimitiveOracleTests` / `ExternalVectorTests` (и точечно в `RuntimeTests`) на байтовых векторах из [fixtures/ber-der/](../fixtures/ber-der/). Round-trip через Roslyn — дополнение, не замена.

На каждый примитив из status § Runtime: DER encode/decode, BER где применимо, round-trip, границы, отказы (чужой тег, truncated, DER indefinite, BOOLEAN не `00`/`FF`, BIT trailing bits).

| API | Минимум |
| --- | --- |
| `TryEncode` / `EncodedLength` | exact fit; short Span → false; равенство с `Encode()` |
| `TryReadOctetString` / `TryReadValue` | fit; short → false; BER constructed OCTET |
| `WriteBoolean` / `ReadBoolean` | DER `00`/`FF`; BER nonzero-as-true |
| `WriteInteger` / `ReadInteger` | `0`, `-1`, 127/128, длинный; empty reject |
| `WriteOctetString` / `ReadOctetString` | empty; long-form; BER constructed + indefinite; ROM overload |
| `WriteNull` / `ReadNull` | empty OK; nonempty reject |
| `WriteObjectIdentifier` / `ReadObjectIdentifier` | OID; arcs; rejects |
| `WriteBitString` / `ReadBitString` | unusedBits; DER trailing-zero; BER constructed |
| `WriteString` / `ReadString` | 12 forms smoke; BER constructed UTF8 |
| `WriteTime` / `ReadTime` | UTC + Generalized; fractionDigits; BER |
| `WriteSequence` / `ReadSequence` | вложенность; OPTIONAL |
| `WriteSet` / `ReadSet` | tag SET |
| `WriteSetOf` | DER sort; BER order |
| `WriteExplicit` | constructed wrapper |
| `WriteAny` / `ReadAny` | IMPLICIT; ContentsMemory |
| `WriteRaw` | append TLV |
| `ReadValue` / `ReadTlv` | owned; wrong tag |
| Wrappers | smoke |
| `Asn1BitString` / `Asn1Any` | Memory/ContentsMemory; equality |
