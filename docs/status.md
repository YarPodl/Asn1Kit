# Что поддержано сейчас

Срез состояния по слоям. Обновляется тем же изменением, которым расширяется поддержка, — иначе документ бесполезен.

## Типы IR

| `kind` | Компилятор | C# backend | Покрытие тестами |
| --- | --- | --- | --- |
| `boolean` | да | да → `bool` | `ParserTests`, `RuntimeTests` |
| `integer` | да, с `namedValues` | да → `BigInteger` | `CompilerTests`, `RoundTripTests` |
| `enumerated` | да, с `values` | да → `BigInteger` (как INTEGER) | — |
| `bitString` | да, с `namedBits` | да → `Asn1BitString`; `namedBits` → `Bit_*` константы | `RuntimeTests`, `RoundTripTests.PrimitivesAsn_*` |
| `octetString` | да | да → `byte[]` | `RoundTripTests`, `RuntimeTests` |
| `oid` | да, dotted-строка | да → `string` | `RuntimeTests.ObjectIdentifier_*` |
| `string` (12 форм `stringType`) | да | да → `string` + `Asn1StringForm` | `RuntimeTests`, `RoundTripTests.PrimitivesAsn_*` |
| `time` (`utc` / `generalized`) | да; `fractionDigits` 0…7 (default 3) для `generalized` | да → `DateTimeOffset` + `Asn1TimeForm`; запись с округлением | `RuntimeTests`, `RoundTripTests.PrimitivesAsn_*` |
| `any` (+ `definedBy`) | да, с проверкой sibling-компонента | да → `Asn1Any` (Tag + Contents; `definedBy` не резолвится) | `ParserTests`, `RuntimeTests.Any_*`, `RoundTripTests.GeneratedCSharp_Any_*`, `ValueResolutionTests.RejectsAnyDefinedByUnknownField` |
| `sequence` | да, `extensible` | да → класс с `Encode` / `Decode` | `RoundTripTests`, `PkixExplicit88Tests` |
| `set` | да | да → класс с `Encode` / `Decode` (DER: порядок по тегу; decode по тегу) | `RoundTripTests`, `ParserTests`, `RuntimeTests` |
| `choice` | да | да → класс + enum `…Kind` | `ParserTests`, `PkixExplicit88Tests` |
| `sequenceOf` | да | да → класс с `List<T> Items` | `ParserTests` |
| `setOf` | да | да → класс с `List<T> Items` (DER: сортировка TLV в `WriteSetOf`) | `RoundTripTests`, `ParserTests`, `RuntimeTests` |
| `ref` | да, с резолвом через модули | да | `PkixExplicit88Tests` |

Неподдержанные бэкендом kind отсекает `CSharpBackend.EnsureBackendSupport` с текстом `C# backend does not support kind '<kind>' yet.` — рекурсивно, включая вложенные компоненты и элементы `SEQUENCE OF` / `SET OF`.

## Значения IR

`integer`, `boolean`, `null`, `oid` (dotted-строка), `string`, `bitString` (`bits` / `hex`), `ref`.

Компилятор резолвит их двухфазно: OID-цепочки разворачиваются, `ub-*` подставляются в `SIZE` и диапазоны, `DEFAULT` с именованным числом разрешается через тип компонента. В скомпилированном IR `ref` обычно уже раскрыт.

## Теги и constraints

- `EXPLICIT` / `IMPLICIT` / `AUTOMATIC TAGS`; `AUTOMATIC` раскрывается в явные `tag` на компонентах, для `CHOICE` тег остаётся `explicit`.
- `SIZE` и диапазоны значений → `constraint.size` / `constraint.value`; `MAX` кодируется отсутствующим `max`.
- `SIZE` сразу после `SEQUENCE` / `SET` без `OF` и `MIN` как конкретная граница — явный отказ.
- Остальные формы (например `FROM`) сохраняются строкой в `constraint.unsupported`.

## Runtime BER/DER

| Операция | `Asn1Writer` | `Asn1Reader` |
| --- | --- | --- |
| BOOLEAN | `WriteBoolean` | `ReadBoolean` |
| INTEGER | `WriteInteger` (`BigInteger`) | `ReadInteger` |
| BIT STRING | `WriteBitString` (`Asn1BitString`) | `ReadBitString` (BER: constructed и indefinite length склеиваются) |
| OCTET STRING | `WriteOctetString` | `ReadOctetString` (BER: constructed и indefinite length склеиваются) |
| NULL | `WriteNull` | `ReadNull` |
| OBJECT IDENTIFIER | `WriteObjectIdentifier` (dotted) | `ReadObjectIdentifier` |
| STRING (12 форм) | `WriteString` + `Asn1StringForm` | `ReadString` (BER: constructed склеивается) |
| TIME (`utc` / `generalized`) | `WriteTime` (`DateTimeOffset`, `fractionDigits` для generalized) | `ReadTime` (дробь 1…7, хвостовые нули ок) |
| SEQUENCE / SET / constructed | `WriteSequence`, `WriteSet` | `ReadSequence`, `ReadSet`, `TryPeekTag`, `Eof` |
| SET OF (DER sort) | `WriteSetOf` | через `ReadSet` |
| EXPLICIT-обёртка | `WriteExplicit` | через `ReadSequence` |
| Готовый TLV | `WriteRaw` | `ReadValue` |
| ANY | `WriteAny` (`Asn1Any`) | `ReadAny` (с ожидаемым тегом или без) |

DER: только definite length, минимальная кодировка INTEGER, BOOLEAN `0x00` / `0xFF`, BIT STRING с нулевыми хвостовыми битами, время только с секундами и суффиксом `Z` (GeneralizedTime: `fractionDigits` 0…7, default 3; на записи без хвостовых нулей дроби). BER на чтении принимает indefinite length, constructed строки/BIT STRING, время без секунд и со смещением `±hhmm`; дробь 1…7 цифр с хвостовыми нулями допускается и в DER.

## Вне профиля компилятора

Явный `CompileException` с позицией: `CLASS` и information object classes, `COMPONENTS OF`, `REAL`, `EXTERNAL`, параметризованные типы.

## Ближайшие пробелы

1. C++ backend и C++ runtime — см. [playbooks/new-backend.md](playbooks/new-backend.md).
2. Резолв `ANY DEFINED BY` в конкретный тип по значению sibling-компонента (сейчас `Asn1Any` остаётся сырым TLV).