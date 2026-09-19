# Схема IR v1

Контракт между компилятором ASN.1 и генераторами кода. Формат — только JSON, схема: [schemas/asn1kit-ir-v1.json](../schemas/asn1kit-ir-v1.json) (Draft 2020-12).

Имена полей — camelCase. `kind` типов — camelCase (`octetString`, `sequenceOf`).

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
| `oid` | Массив дуг, если OID был в исходнике |
| `tagDefault` | `explicit`, `implicit` или `automatic` |
| `imports` | `{ module, types[] }` |
| `options` | Свободный словарь |
| `types` | Именованные определения `{ name, type, options? }` |

## Выражение типа (`type`)

Дискриминатор `kind`:

- `boolean`, `null`, `octetString`, `oid`
- `integer` — опционально `namedValues: [{ name, value }]`
- `enumerated` — `values: [{ name, value }]`
- `sequence` / `choice` — `components[]`
- `sequenceOf` — `element` (вложенное выражение)
- `ref` — `name`, опционально `module`

У любого выражения опциональны `tag` и `options`.

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

## Компонент SEQUENCE / CHOICE

`name`, `type`, `optional` (для CHOICE всегда `false`), `options`.

## Options

Объект с `additionalProperties: true`. Неизвестные ключи сохраняются при round-trip.

Рекомендуемая вложенность по бэкенду:

| Путь | Где | Смысл |
| --- | --- | --- |
| `options.csharp.namespace` | модуль | Namespace |
| `options.csharp.typeName` | тип | Имя класса |
| `options.csharp.propertyName` | поле | Имя свойства |
| `options.generate` | тип | `false` — не генерировать |

## Пример

См. [fixtures/ir/example.json](../fixtures/ir/example.json).
