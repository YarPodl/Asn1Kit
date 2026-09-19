# Схема IR v1

Человекочитаемое представление ASN.1-модуля. Кодировка файла: YAML (по умолчанию) или JSON. Имена полей — camelCase.

## Корневой объект

| Поле | Тип | Описание |
| --- | --- | --- |
| `irVersion` | number | Сейчас `1` |
| `module` | string | Имя модуля |
| `oid` | string? | Object identifier модуля, если был в исходнике |
| `source` | string? | Путь исходного файла |
| `tagDefault` | string | `explicit`, `implicit` или `automatic` |
| `imports` | array | `{ module, types[] }` |
| `options` | object | Свободный словарь |
| `types` | array | Нормализованные типы |

Неизвестные ключи в `options` не отбрасываются.

## Тип (`types[]`)

| Поле | Описание |
| --- | --- |
| `name` | typereference |
| `kind` | `sequence`, `choice`, `sequence-of`, `alias` |
| `type` | Для `alias`: имя builtin или другого типа |
| `elementType` | Для `sequence-of` |
| `tag` | Опциональный тег самого типа |
| `namedNumbers` | Именованные значения INTEGER |
| `fields` | Поля SEQUENCE/CHOICE |
| `options` | Свободный словарь |

## Поле

| Поле | Описание |
| --- | --- |
| `name` | identifier |
| `type` | Builtin (`INTEGER`, `OCTET STRING`, …) или имя типа |
| `optional` | Только для SEQUENCE |
| `tag` | Назначенный или явный тег |
| `options` | Свободный словарь |

## Тег

```yaml
tag:
  class: context   # universal | application | context | private
  number: 0
  mode: implicit   # implicit | explicit
```

## Известные options

| Ключ | Где | Смысл |
| --- | --- | --- |
| `csharp.namespace` | модуль | Namespace сгенерированного кода |
| `csharp.typeName` | тип | Имя класса |
| `csharp.propertyName` | поле | Имя свойства |
| `generate` | тип | `false` — не генерировать |

Компилятор заполняет `csharp.namespace` и `csharp.typeName` значениями по умолчанию. Их можно изменить вручную.

## Пример

См. [fixtures/ir/example.asn1.yaml](../fixtures/ir/example.asn1.yaml).
