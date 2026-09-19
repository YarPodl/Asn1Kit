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
| `time` (`utc` / `generalized`) | да | да → `DateTimeOffset` + `Asn1TimeForm` | `RuntimeTests`, `RoundTripTests.PrimitivesAsn_*` |
| `any` (+ `definedBy`) | да, с проверкой sibling-компонента | **нет** | `ParserTests`, `ValueResolutionTests.RejectsAnyDefinedByUnknownField` |
| `sequence` | да, `extensible` | да → класс с `Encode` / `Decode` | `RoundTripTests`, `PkixExplicit88Tests` |
| `set` | да | **нет** | `ParserTests` |
| `choice` | да | да → класс + enum `…Kind` | `ParserTests`, `PkixExplicit88Tests` |
| `sequenceOf` | да | да → класс с `List<T> Items` | `ParserTests` |
| `setOf` | да | **нет** | `ParserTests.BackendRejectsUnsupportedKinds` |
| `ref` | да, с резолвом через модули | да | `PkixExplicit88Tests` |

Неподдержанные бэкендом kind отсекает `CSharpBackend.EnsureBackendSupport` с текстом `C# backend does not support kind '<kind>' yet.` — рекурсивно, включая вложенные компоненты и элементы `SEQUENCE OF`.

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
| TIME (`utc` / `generalized`) | `WriteTime` (`DateTimeOffset`) | `ReadTime` |
| SEQUENCE / constructed | `WriteSequence` | `ReadSequence`, `TryPeekTag`, `Eof` |
| EXPLICIT-обёртка | `WriteExplicit` | через `ReadSequence` |
| Готовый TLV | `WriteRaw` | `ReadValue` |

DER: только definite length, минимальная кодировка INTEGER, BOOLEAN `0x00` / `0xFF`, BIT STRING с нулевыми хвостовыми битами, время только с секундами и суффиксом `Z`. BER на чтении принимает indefinite length, constructed строки/BIT STRING, время без секунд и со смещением `±hhmm`.

## Вне профиля компилятора

Явный `CompileException` с позицией: `CLASS` и information object classes, `COMPONENTS OF`, `REAL`, `EXTERNAL`, параметризованные типы.

## Ближайшие пробелы

1. `set` / `setOf` в C# backend (для `SET OF` в DER нужна каноническая сортировка элементов).
2. `any` / `ANY DEFINED BY` в генерации — требует решения, как отдавать сырой TLV наружу.
3. C++ backend и C++ runtime — см. [playbooks/new-backend.md](playbooks/new-backend.md).
