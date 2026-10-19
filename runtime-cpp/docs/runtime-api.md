# C++ runtime API (фаза 2 — примитивы + constructed scopes)

Публичный namespace `asn1kit`, методы в `snake_case`. Wire-правила BER/DER и soft-profile совпадают с эталонным runtime репозитория; layer-1 векторы — общие JSON в [`runtime-csharp/fixtures/ber-der`](../../runtime-csharp/fixtures/ber-der/).

## `bytes` vs `span`

| Тип | Роль |
| --- | --- |
| `std::span<const uint8_t>` | Эфемерный borrow: входы encode, `try_encode` destination. Lifetime — на вызывающем. |
| `asn1kit::bytes` | Окно (`span`) + type-erased keep-alive источника (`shared_ptr<void>`). |

Фабрики `bytes`:

- `borrow(span)` — без keep-alive (горячий ephemeral путь).
- `from_shared(shared_ptr<T>, span)` / `from_vector` — продлевают жизнь источника.
- `copy_from(span)` — owned копия.
- `slice(offset[, length])` — наследует тот же owner.

`reader` принимает `bytes` (и удобные обёртки). Возвраты `read_octet_string`, INTEGER/BIT STRING contents — `bytes`-срезы входного owner’а (или новый owned буфер после BER concat).

Мутация исходных октетов, пока живы views — UB. Detach: `to_vector()` / `copy_from`.

## Константные OID

`constexpr auto id = asn1kit::oid_literal<"1.3.6.1.5.5.7">();` проверяет и кодирует OID через `consteval`. `id.contents` — статический `std::array` точного размера с DER-содержимым (без тега и длины), `id.span()` доступен в constant expression. `id.get()` или `id()` возвращает обычный `oid`, ссылающийся на эти байты без разбора строки, выделения памяти и копирования. Байты живут независимо от объекта константы, включая временный объект.

Литералы и динамический `oid::parse` используют общий кодек; сохраняется профиль дуг runtime: неотрицательный `int`, включая ограничение `40 * arc0 + arc1 <= INT_MAX`. Неверный литерал приводит к ошибке компиляции. Буферная перегрузка `oid::encode_contents` и `get_encode_contents_max_length` доступны как `constexpr`; динамические строки по-прежнему поддержаны.

Генератор объявляет одну константу: `static constexpr auto IdPkix = asn1kit::oid_literal<"1.3.6.1.5.5.7">();`. `IdPkix.text()` возвращает C-строку со статическим временем жизни, `IdPkix.span()` — DER-содержимое, `IdPkix.get()` — runtime `oid`. При переходе со старого каталога текстовый `IdPkix` заменяется на `IdPkix.text()`, а вызов `IdPkix_oid()` — на `IdPkix.get()`; отдельного имени с суффиксом `_oid` больше нет.

## Инвентарь

| Область | API |
| --- | --- |
| Tag / encoding | `tag`, `tag_class`, `encoding`, `string_form`, `time_form`, `exception` |
| Soft-read / hostile BER | `reader_options` — `default_profile`, `strict`, `allow_non_minimal_length`, `allow_overlong_oid_base128`; `max_recursion_depth`, `max_materialized_constructed_value_bytes` |
| Writer primitives | `write_boolean`, `write_integer` (i32/u32/i64/u64/`integer`), `write_integer_decimal`, `write_enumerated_decimal`, `write_octet_string`, `write_null`, `write_object_identifier`, `write_bit_string`, `write_string`, `write_time`, `write_raw`, `encode` / `try_encode` / `reset` |
| Writer scopes | `enter_sequence` / `enter_set` / `enter_sequence_of` / `enter_set_of` / `enter_explicit` → `writer_scope`; `write_sequence_of` / `write_set_of` (template + range + invocable) |
| Reader primitives | соответствующие `read_*`, `try_peek_tag`, `next_is`, `next_matches<T>`, `eof`, `throw_if_not_empty` |
| Reader scopes (низкий) | `enter_sequence` / `enter_set` / `enter_explicit` / `enter_encoded(bytes)` → `reader_scope` |
| Reader scopes (рекомендуемый) | `with_sequence` / `with_set` / `with_explicit` / `with_encoded` — enter → callback → `throw_if_not_empty` на success → pop |
| Reader OF (callback) | `for_each_sequence_of` / `for_each_set_of`; `read_sequence_of` / `read_set_of` + invocable |
| Reader type-driven | `read<T>` / `read_from_explicit<T>` / `read_sequence_of<T>` / `read_set_of<T>` / `read_sequence_of_from_explicit<T>` — без лямбд (T = generated или примитив) |
| Writer type-driven | `write` / `write_to_explicit` / `write_sequence_of` / `write_set_of` / `write_sequence_of_to_explicit` — без лямбд |
| Ergonomic encode/decode (`encode_decode.hpp`) | `encode(value, enc)` (значение → `std::vector<uint8_t>`); `try_encode(destination, value[, bytes_written], enc)` — запись в вызывающий буфер, `false` при нехватке места; `decode` (TLV → значение, строгий EOF, trailing → `asn1kit::exception`); `decode_prefix` игнорирует хвост. Перегрузки: `std::span<const uint8_t>`, `std::vector<uint8_t>`, `bytes` |
| Value types | `integer`, `bit_string`, `oid`, `null_value`, `utc_date_time`, `any` |
| ANY | `any` — полный TLV (`tag` + `encoded` + `contents`); `read_any` / `write_any` (+ IMPLICIT retag); `from_tag_and_contents` / `from_value` / `decode_value` |
| Containing | Известный `T`: `read_octet_string_containing` / `read_bit_string_containing` (+ type-driven без лямбды через `read<T>()`) и симметричные `write_*`; неизвестное содержимое остаётся `bytes` / `bit_string` без неявной ASN.1-валидации |
| Open type | Primary generated API — functional view: `value.<table>().<entry>()`, mutable `value.<table>().<entry>(payload)`, `Type::make_<table>().<entry>(payload)`, rvalue decode `Type::decode(r).<table>().<entry>()` (`open_owned_target`, decode-only). Вложенная навигация с rvalue переносит carrier в дочерний view; повторные contextual routes одной таблицы различаются route-суффиксом. Contextual views read-only. View эмитится для direct sites всегда; для contextual — только без inline-границы и с ровно одним structural сегментом пути до open-поля, иначе остаётся `try_decode_<route>`. `open_function_view` + NTTP binding; прямые descriptor-каталоги живут в module `detail`, emitted registry-иерархии нет. Extension: `open_binding<T>(key)` + carrier `try_decode` / `set` / `create` (`default_open_context`). Также `encode_open` / `decode_open`, `asn_string<form>` / `asn_time<form>`, `decode_contained`, `encode_contained_*`, `decode_each` / `encode_each` |
| Time | `utc_date_time` (календарные поля UTC + `time_fraction` до 7 десятичных цифр); `constexpr`-конверсии в `std::chrono`: `to_sys_time` / `from_sys_time` (`utc_time_point`, 100 нс, 64-битный диапазон) и `to_system_clock` / `from_system_clock` (`std::chrono::system_clock::time_point`) |

**Value / Lazy:** `asn1kit::value<T>` (`value.hpp`) — eager decode + `original_encoding()`; non-const `get()` сбрасывает retained TLV (encode идёт из текущего значения). `asn1kit::lazy<T>` (`lazy.hpp`) — deferred decode, `has_encoded()` / `write_to`. Reader: `read_with_original_encoding` / `read_lazy` (callback или type-driven `()` / `(tag)` через `read<T>()`). Generated encode: при `has_original_encoding()` / `has_encoded()` пишет raw TLV.

Вне текущего среза: modern IOC, oracle, `external/` фикстуры. Extensible SEQUENCE/SET/CHOICE эмитит codegen (skip-unknown через `read_any`). Codegen C++ — [cpp-backend.md](../../compiler/docs/playbooks/cpp-backend.md).

## Debug-формат (`format.hpp`)

`asn1kit/format.hpp` — header-only потоковый дамп для отладки. Публичная точка входа — `template <typename T> void to_string(const T& value, std::ostream& out)`, без завершающего `\n`. Точка кастомизации (ADL) — `void to_string(const T& value, std::ostream& out, int indent)` в namespace типа; отдельного generic-шаблона с `(…, int)` нет, иначе ADL уходит в бесконечную рекурсию.

- Перегрузки: `bool`, `int32_t`/`uint32_t`/`int64_t`/`uint64_t`, `std::string`, `std::string_view`, `integer`, `null_value`, `oid`, `bytes`, `bit_string`, `utc_date_time`, `any`, `std::optional<T>`, `std::vector<T>`, `value<T>`, `lazy<T>`.
- Числа — locale-independent (`std::to_chars` / `integer::to_decimal`), OID — dotted, время — `utc_date_time::to_iso()`, `any` — `any::to_string()`.
- Грамматика: структурный тип — `Type {`, по строке `field: <dump>` с отступом 4 пробела на уровень, `}`; CHOICE — `Type {` / `kind: <alt>` / `value: <dump>`; `OF` — `[N] { … }`; named BIT STRING — `Flags { N bits }`; `bytes` → `N bytes`; `bit_string` → `N bits`; `null` → `null`; отсутствующий `optional` → `<absent>`; lazy без TLV → `<unset>`, с TLV → `<not decoded: N bytes>`; opaque `contained` → `<opaque: N bytes>`.
- Codegen эмитит перегрузки для своих именованных типов в `{Module}.format.hpp` ([cpp-backend.md](../../compiler/docs/playbooks/cpp-backend.md)).

## Constructed scopes

- Scopes **move-only**. Публично — `end()` и destructor.
- Writer: `end()` бросает `std::logic_error` при LIFO / double-end; destructor `noexcept` финализирует frame и при misuse вызывает `std::terminate`. При открытом scope запрещены `encode` / `try_encode` / `reset` (`std::logic_error`).
- Reader: push/pop окна на одном объекте; double-end идемпотентен; LIFO-нарушение в `end()` → `std::logic_error`. Destructor scope **не** проверяет empty.
- `with_*`: на успешном return callback’а — `throw_if_not_empty`; при исключении из callback — только pop (исходная ошибка не заменяется). Нужен для составных тел/`DEFAULT`/`OPTIONAL` peek-логики.
- `next_is(tag)` — `try_peek_tag` + `matches_ignore_constructed` (примитивы / явный тег поля).
- `next_matches<T>()` — `try_peek_tag` + `T::match_tag` (OPTIONAL/DEFAULT именованных SEQUENCE/SET/CHOICE).
- Type-driven (`concepts.hpp` + `detail/typed_value.hpp`): generated-типы (`match_tag`; SEQUENCE/SET — `decode`/`encode` с default natural tag; CHOICE — отдельные натуральный и tagged overload’ы) и примитивы (`bool`, fixed integers, `integer`, `bytes`, `oid`, `bit_string`). Untagged CHOICE входит в `asn1_decodable`/`asn1_encodable` без единого wire-тега. Codegen: `if (r.next_matches<T>()) …`, `r.read_sequence_of<T>()`, `w.write_to_explicit(t, value)`.
- `enter_set_of` / `write_set_of`: DER сортирует TLV contents лексикографически; BER сохраняет порядок. Обычный `enter_set` порядок полей не меняет.
- `enter_*` помечены `[[nodiscard]]`.

Wire-ошибки (тег, length, trailing data) — `asn1kit::exception`. API misuse scopes — `std::logic_error`.

## Soft-read

Default принимает: non-minimal INTEGER, nonzero BIT STRING trailing bits.  
Default отклоняет: non-minimal length, overlong OID base-128.  
Constructed OCTET/BIT/string soft-accept и под DER (как у эталонного runtime).

Строковые значения в `std::string` содержат валидный UTF-8. UTF8String отвергает overlong, surrogate и scalar выше `U+10FFFF`; BMPString — BMP/UCS-2 без surrogate code units; UniversalString поддерживает scalar до `U+10FFFF`. Latin-1 формы при encode отвергают непредставимые символы.

UTCTime/GeneralizedTime проверяют Gregorian calendar; при записи UTCTime допустимы только годы `1950…2049`, однозначно представимые его двузначным годом. BER offset и округление GeneralizedTime корректно переносятся через границы дня, месяца, високосного года и года. OID использует неотрицательные `int`-дуги, включая ограничение `40 * arc0 + arc1 <= INT_MAX`.

Encode всегда канонический DER (definite length).

## Лимиты hostile BER

`reader_options::max_recursion_depth` ограничивает рекурсивный поиск EOC во вложенных indefinite-length TLV и вложенность constructed OCTET STRING / BIT STRING / строк при materialize. Default `reader_options::default_max_recursion_depth` равен 64; значение должно быть положительным. Ровно 64 уровня разрешены, следующий уровень отклоняется с `asn1kit::exception`.

`reader_options::max_materialized_constructed_value_bytes` ограничивает сумму payload всех primitive-сегментов одного materialized constructed OCTET STRING, BIT STRING, строкового или временного значения. Default `reader_options::default_max_materialized_constructed_value_bytes` равен 100 MiB; ноль разрешает только пустой constructed payload. Граница включительна, превышение отклоняется до выделения результирующего `std::vector`. Primitive values остаются `bytes`-view исходного буфера и этот бюджет не расходуют.

Лимиты действуют одинаково для definite и indefinite constructed materialization и наследуются вложенными reader-окнами. `try_read_octet_string` при превышении лимита бросает `asn1kit::exception` без продвижения reader; `false` означает только короткий destination.

## `exception`

Ошибки encode/decode — `asn1kit::exception` (`std::runtime_error`).

На decode-path исключение может нести **absolute offset** во входном буфере root-`reader` (вложенные constructed-окна учитывают base вложенности):

- `has_offset()` / `offset()` — позиция TLV/EOF во входном буфере;
- `what()` = reason + суффикс ` (offset=N)` при наличии offset.

Ошибки разбора contents примитива остаются message-only. Encode без курсора — message-only (`has_offset() == false`).

## Внутренности

`encode_buffer`, `decode_cursor`, `constructed_decoder`, `text_codec` — не публичный ABI; заголовки в `include/asn1kit/detail/` и `src/`.
