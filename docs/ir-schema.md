# Схема IR v1

Контракт между компилятором ASN.1 и генераторами кода. Формат — только JSON, схема: [schemas/asn1kit-ir-v1.json](../schemas/asn1kit-ir-v1.json) (Draft 2020-12).

Имена полей — camelCase. `kind` типов — camelCase (`octetString`, `sequenceOf`, `bitString`).

Расширения аддитивны: `irVersion` остаётся `1`, старые документы валидны.

## Корень

| Поле | Тип | Описание |
| --- | --- | --- |
| `irVersion` | number | Сейчас `1`. Несовместимые изменения → `2`. |
| `sourceFiles` | string[]? | Имена исходных файлов (диагностика). |
| `options` | object? | Свободный словарь. |
| `modules` | array | Модули (несколько — для `IMPORTS`). |

## Модуль

| Поле | Описание |
| --- | --- |
| `name` | Имя модуля |
| `oid` | Dotted-строка (`"1.3.6.1"`), если OID был в исходнике |
| `tagDefault` | `explicit`, `implicit` или `automatic` |
| `imports` | `{ module, types[], values? }` |
| `options` | Свободный словарь |
| `types` | Именованные определения `{ name, type, options? }` |
| `values` | Value assignments `{ name, type, value, options? }` |

## Выражение типа (`type`)

Дискриминатор `kind`:

- `boolean`, `null`, `octetString`, `oid`
- `integer` — опционально `namedValues: [{ name, value }]`
- `enumerated` — `values: [{ name, value }]`, опционально `extensible`
- `bitString` — опционально `namedBits: [{ name, value }]`
- `string` — обязательно `stringType`: `utf8` \| `printable` \| `teletex` \| `t61` \| `ia5` \| `numeric` \| `visible` \| `bmp` \| `universal` \| `general` \| `graphic` \| `videotex`
- `time` — обязательно `timeType`: `utc` \| `generalized`; опционально `fractionDigits` `0…7` (только `generalized`; отсутствие = 3 при записи)
- `any` — опционально `definedBy` (имя sibling-компонента); опционально `bindings: [{ key, name?, type }]` — таблица open-type (ключ — dotted OID или десятичный INTEGER, `name` задаёт семантическое имя альтернативы); заполняется overlay после compile, не парсером ASN.1 1988. C# при `bindings` эмитит `Owner_Field` с `From…`/`Unknown`. На каждый различный CLR-тип создаётся не более одного свойства; если несколько семантических альтернатив имеют один CLR-тип, соседний `Owner_FieldKind` сохраняет выбранную ASN.1-альтернативу. `options.openType.mismatch`: `soft` \| `strict` (default `soft`).
- `sequence` / `set` / `choice` — `components[]`, опционально `extensible`
- `sequenceOf` / `setOf` — `element`
- `ref` — `name`, опционально `module`

У любого выражения опциональны `tag`, `constraint`, `options`.

### Результат разрешения IOC и параметризации

CLASS, WITH SYNTAX, information objects/sets и формальные параметры остаются внутри компилятора. В `module.types` записываются только структурно различные конкретные типы; специализации, различающиеся лишь IOC-таблицами, используют общий тип. `ref.openTypes` хранит таблицу для конкретного места использования: `[{ path: string[], bindings: [{ key, name?, type }], table?, tableExtensible? }]`. Путь идёт от целевого типа ссылки по именам компонентов; `[]` обозначает элемент OF, `containing` — содержимое OCTET/BIT STRING. Опциональный `table` — имя ASN.1 object set, если таблица взята из именованной ссылки (`SignatureAlgorithms`, `CertExtensions`); анонимные `{...}` и объединения имя не получают. У общих типов соответствующий `any` не содержит `bindings`, `table` и `tableExtensible`. Bindings могут поступать из ASN.1 object sets и из legacy overlay.

`typeDef.specialization: { module, name }` у структурно различной специализации сообщает исходный шаблон. Это семантическая связь, без C#-имён или правил кодирования; генератор может выделить общую основу для семейства, например `SIGNED`.

Новые семантические поля не помещаются в `options`:

| Поле | Где | Контракт |
| --- | --- | --- |
| `selector: { levels, path[] }` | `any` | `levels = 0` — владелец, `1` — его родитель и т.д.; считаются SEQUENCE/SET/CHOICE, OF прозрачен. `path` — непустой путь имён компонентов к OID/INTEGER. |
| `tableExtensible: boolean` | `any` | Наличие поля обозначает современную IOC-таблицу, значение сохраняет её `...`. Неизвестный ключ всегда raw; закрытость пока не исполняется. Отсутствие поля сохраняет legacy fallback/mismatch. |
| `table: string` | `any`, `ref.openTypes[]` | Имя именованного object set, породившего bindings. Для codegen — человекочитаемый stem каталога; на идентичность таблицы не влияет. |
| `containing: type` | `octetString`, `bitString` | Содержимое закодировано внутри contents внешнего string-типа; может быть `any` с selector/bindings. BIT STRING с типизированным содержимым выровнен по октетам. |
| `extensionAddition: true` | компонент | Компонент между первой и второй границами расширения; компоненты trailing root не получают эту метку. |
| `extensionGroup: integer >= 0` | компонент addition | Принадлежность к группе `[[n: ...]]`; номер без явно заданной версии назначается компилятором. |

`definedBy` сохраняется для старых документов. OF и CONTAINING сохраняют контекст владельца при валидации selector. Синтаксические уровни `@`, `@.`, `@..` преобразуются компилятором в эти разрешённые уровни IR. Отсутствие bindings или пустой extensible set не добавляет типы из импортированных модулей.

```json
{
  "class": "context",
  "number": 0,
  "mode": "implicit"
}
```

`class`: `universal` | `application` | `context` | `private`.  
`mode`: `implicit` | `explicit`.

Компилятор раскрывает `AUTOMATIC TAGS` в явные `tag` на компонентах. Генератор правила X.680 не повторяет.

### Constraint

```json
{
  "size": { "min": 1, "max": 64 },
  "value": { "min": 0, "max": 256 },
  "unsupported": "(FROM (\"a\"..\"z\"))"
}
```

- `size` / `value`: `{ min, max? }` — отсутствие `max` означает `MAX`.
- Ссылки на value assignments (`ub-name`) разрешаются компилятором в числа.
- Немоделируемые формы (например `FROM`) сохраняются в `unsupported`.

## Компонент SEQUENCE / SET / CHOICE

`name`, `type`, `optional`, `default?` (выражение значения), `options`.

Для CHOICE `optional` / `default` запрещены. `DEFAULT` в ASN.1 делает компонент optional и записывает разрешённое значение в `default`.

## Значения (`module.values` и `default`)

Дискриминатор `kind`:

| kind | Поля |
| --- | --- |
| `integer` | `value` |
| `boolean` | `value` |
| `null` | — |
| `oid` | `value: string` dotted-форма (`"1.3.6.1"`; цепочки вроде `{ id-pkix 1 }` уже развёрнуты) |
| `string` | `value` |
| `bitString` | `bits?` / `hex?` |
| `octetString` | `hex` (contents, без тега/длины) |
| `structured` | `fields: { componentName: value }` для SEQUENCE/SET; отсутствующие DEFAULT/OPTIONAL поля допустимы |
| `collection` | `items: value[]` для OF |
| `choice` | `alternative: string`, `value` |
| `typed` | `type: type`, `value` — конкретная структура открытого значения (`Type : value`) |
| `ref` | `name`, опционально `module` (в скомпилированном IR обычно уже разрешён) |

## Options

Объект с `additionalProperties: true`. Неизвестные ключи сохраняются при round-trip.

Рекомендуемая вложенность по бэкенду:

| Путь | Где | Смысл |
| --- | --- | --- |
| `options.csharp.namespace` | модуль | Namespace |
| `options.csharp.typeName` | тип | Имя класса |
| `options.csharp.propertyName` | поле | Имя свойства |
| `options.generate` | тип | `false` — не генерировать |
| `options.integer.representation` | тип / модуль | Представление INTEGER: `int32` \| `uint32` \| `int64` \| `uint64` \| `bigint` \| `der`. На типе перекрывает модуль. Если не задано, C# backend выводит: при `namedValues` → `int32` (или `int64` при метке вне `int`); иначе из полного `constraint.value`; иначе `der`. |
| `options.openType.mismatch` | модуль / документ / legacy `any` | При известном ключе bindings, если **тег** TLV не совпал с типом: `soft` (default) → `Unknown`/`Asn1Any`; `strict` → `Asn1Exception`. Современные таблицы всегда отвергают несовместимый известный тип. |
| `options.lazy` | поле / тип / модуль | `true` — отложенный разбор SEQUENCE/SET и SEQUENCE OF/SET OF (C#: `Asn1Lazy<T>` / `Asn1Lazy<T[]>`). Разрешение: component → TypeExpr → typedef → module; default `false`. |
| `options.retainEncoded` | поле / тип / модуль | `true` — eager-разбор SEQUENCE/SET/OF с сохранением исходного полного TLV для хеширования или проверки подписи (C#: `Asn1Value<T>` / `Asn1Value<T[]>`). Encode всегда строится из текущего `Value`. Игнорируется, если `lazy` уже включён. Та же цепочка разрешения, что у `lazy`; default `false`. |

Для специализации `SIGNED<T>` опция на поле `toBeSigned` сохраняет привычное свойство `ToBeSigned: T` и добавляет `ToBeSignedOriginalEncoding: ReadOnlyMemory<byte>` с исходным TLV. Остальные поля используют `Asn1Value<T>`.
| `options.csharp.valueType` | typedef SEQUENCE/SET | `true` — эмит `struct` вместо `sealed class`. |

Sidecar open-type bindings (CLI `--bindings`, API `OpenTypeBindings`) адресует поле как
`Module.Type.field`. Для прямого `ANY DEFINED BY` значение остаётся массивом bindings. Если поле
ссылается на typedef вида `AttributeValue ::= ANY`, объектная форма локально раскрывает alias и
задаёт sibling-дискриминатор, не изменяя typedef и остальные его использования:

```json
{
  "Module.AttributeTypeAndValue.value": {
    "definedBy": "type",
    "bindings": [
      { "key": "2.5.4.3", "type": { "kind": "string", "stringType": "utf8" } }
    ]
  }
}
```

Sidecar options-patch (CLI `--patch`, API `IrOptionsPatch`): JSON `{ "modules": { "<Module>": { … } }, "fields": { "<Module>.<Type>.<field>": { … } }, "types": { "<Module>.<Type>": { … } } }` — deep-merge в `options`. Неизвестный module/type/field → ошибка. Пример: [dvcs.patch.json](../compiler/fixtures/ir/dvcs.patch.json).

## Примеры

- [compiler/fixtures/ir/example.json](../compiler/fixtures/ir/example.json) — минимальный SEQUENCE
- [compiler/fixtures/ir/pkix1-explicit88.json](../compiler/fixtures/ir/pkix1-explicit88.json) — RFC 5280 Appendix A.1
- [compiler/fixtures/ir/dvcs.patch.json](../compiler/fixtures/ir/dvcs.patch.json) — продуктовые namespaces / lazy / retainEncoded / valueType для PKIX/CMS/DVCS
