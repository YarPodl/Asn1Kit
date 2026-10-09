# Журнал решений

Почему устроено именно так. Читать до того, как «упростить» что-то из перечисленного.

Формат записи: решение — причина — последствие.

## IR — только JSON со схемой Draft 2020-12

**Причина.** IR правится человеком между `compile` и `generate` (namespace, имена типов, `generate: false`), поэтому формат должен быть текстовым и проверяемым без запуска генератора. Схема в [schemas/asn1kit-ir-v1.json](../schemas/asn1kit-ir-v1.json) — исполняемый контракт, а не описание.

**Последствие.** Любой путь, создающий IR, обязан проходить `IrSerializer.ValidateSchema`. Схема встраивается в сборку `Asn1Kit.Ir` как ресурс и ищется на диске только как запасной вариант, чтобы CLI работал из любого каталога.

## Версия IR меняется целиком, а не «по месту»

**Причина.** Документы, сохранённые пользователем, должны продолжать компилироваться после обновления инструмента.

**Последствие.** При `irVersion: 1` допустимы только аддитивные правки схемы. Несовместимое изменение — новая схема v2 рядом со старой. `IrValidator` отвергает всё, кроме `irVersion == 1`.

## `options` — свободный словарь с сохранением неизвестных ключей

**Причина.** Бэкендов будет больше одного (C#, затем C++), и каждый хочет свои настройки. Централизованная типизация опций заставляла бы менять `Asn1Kit.Ir` ради каждого бэкенда.

**Последствие.** `additionalProperties: true` и тест `IrSchemaTests.UnknownOptions_ArePreserved`. Чужие ключи нельзя терять при round-trip, даже если текущий бэкенд их не понимает.

## Резолв значений двухфазный

**Причина.** В реальных модулях ссылки идут вперёд: в PKIX1Explicit88 `ub-name` и прочие upper bounds объявлены в конце файла, а используются в `SIZE` задолго до объявления.

**Последствие.** `IrBuilder` в конструкторе собирает все type/value assignments всех модулей, и только потом разворачивает OID-цепочки, подставляет `ub-*` и разрешает `DEFAULT`. Однопроходный резолв «по ходу разбора» вернёт ошибку на валидном модуле.

## OID в IR — dotted-строка, арифметика дуг живёт в runtime

**Причина.** Массив дуг в IR провоцировал дублирование логики: компилятор склеивал цепочки, генератор снова резал массив, runtime считал base-128. Строка `"1.3.6.1.5.5.7.48.1"` — одно представление на весь конвейер.

**Последствие.** `IrOidValue.Value` и `IrModule.Oid` — строки; `Asn1Oid.ParseArcs` / `EncodeContents` в runtime — единственное место, где OID превращается в числа и байты. Невалидная строка падает `Asn1Exception` там же.

## Golden-фикстура PKIX сравнивается как нормализованный JSON

**Причина.** Нужен дешёвый детектор незаметных регрессий компилятора на модуле реального размера (~700 строк ASN.1). Точечные assert такого не ловят.

**Последствие.** `PkixExplicit88Tests.CompilesAndMatchesGoldenIr` прогоняет golden через `FromJson` → `ToJson` и сравнивает со свежим выводом строкой: форматирование и порядок полей на результат не влияют, а вот любое изменение формы IR — влияет. Фикстура пересобирается через CLI, её diff смотрится глазами при ревью.

## Вне профиля — явный отказ, а не частичный разбор

**Причина.** Молча пропущенная конструкция даёт IR, который выглядит правильным, и неверный сгенерированный кодек. Ошибка обнаружится на чужих данных.

**Последствие.** `COMPONENTS OF`, `REAL`, `EXTERNAL` и формы IOC вне документированного профиля роняют `CompileException` с позицией. Нераспознанные формы constraint не отбрасываются, а сохраняются в `constraint.unsupported` — данные не теряются, но и не притворяются понятыми.

## Неподдержанный бэкендом kind падает до генерации

**Причина.** Компилятор ушёл вперёд бэкенда (см. [status.md](status.md)), и половина kind в C# ещё не реализована.

**Последствие.** `CSharpBackend.EnsureBackendSupport` рекурсивно обходит тип до эмита и бросает `NotSupportedException` с именем kind. Лучше отказ на входе, чем частично сгенерированный файл, который не компилируется.

## Кодек только в runtime, генератор его не дублирует

**Причина.** Два места с правилами DER разъезжаются; баг придётся чинить дважды и в сгенерированном коде у пользователя.

**Последствие.** В шаблонах бэкенда нет байтов тегов и длин — только вызовы `Asn1Writer` / `Asn1Reader` (`Write*` / `Read*`). Статические обёртки в `Asn1Primitives` — convenience для тестов и приложений; codegen их не эмитит. Сгенерированный код тестируется компиляцией через Roslyn в `RoundTripTests`.

## SET / SET OF — DER-порядок в разных слоях

**Причина.** X.690 §11.5 требует порядок компонентов SET по тегу; §11.6 — лексикографический порядок полных TLV элементов SET OF. Порядок компонентов известен на генерации, значения элементов SET OF — только в runtime.

**Последствие.** C# backend эмитит encode SET в порядке тегов и decode по `TryPeekTag`. SET OF пишет элементы внутри `Asn1Writer.EnterSetOf(tag)`; runtime в DER режиме разбирает содержимое на полные TLV и сортирует лексикографически. На чтении порядок не валидируется (как и для прочих типов).

## Время — `DateTimeOffset`, UTCTime с пивотом RFC 5280

**Причина.** Подписанные PKIX-структуры несут `UTCTime` / `GeneralizedTime`; вызывающему коду нужен календарный момент, а не сырая строка. Двузначный год UTCTime без пивота неоднозначен.

**Последствие.** Runtime пишет DER-форму по X.690 §11.7 (`YYMMDDHHMMSSZ` / `YYYYMMDDHHMMSS[.f…fffffff]Z`: секунды обязательны, суффикс `Z`, точка). Точность дроби GeneralizedTime задаётся IR-полем `fractionDigits` (`0…7`, default **3**): момент округляется до этой доли секунды, затем хвостовые нули срезаются; нулевая дробь и точка опускаются. На чтении дробь длины 1…7 принимается всегда, в том числе с хвостовыми нулями (DER это не отвергает). BER дополнительно допускает пропуск секунд, смещение `±hhmm` и запятую. Год UTCTime: `00–49` → 20xx, `50–99` → 19xx. Исходная строка не сохраняется — точный re-encode подписанного значения не гарантируется, если вход был не-DER.

## Строки Teletex/T61/Videotex/Graphic/General — Latin-1

**Причина.** Полноценный T.61 и связанные наборы символов почти не встречаются в современных модулях, а их корректная реализация дороже пользы для профиля v0.

**Последствие.** Эти формы кодируются и декодируются как ISO-8859-1 без проверки набора: 8-битные октеты переживают round-trip без потерь. `Printable` / `Numeric` / `IA5` / `Visible` по-прежнему валидируют допустимые символы; `UTF8` / `BMP` / `Universal` используют UTF-8 / UTF-16BE / UTF-32BE.

## Decode — zero-copy `ReadOnlyMemory` на буфер reader

**Причина.** Owned-копия каждого OCTET / ANY / INTEGER / BIT STRING на горячем пути дороже пользы: буфер уже есть у вызывающего (сертификат, CMS, PDU). Безопасное долговременное хранение без исходных байтов — отдельный сценарий, его закрывает явный detach.

**Последствие.** `Asn1Reader` держит входной `ReadOnlyMemory<byte>` внутри value-type cursor; значения из `Read*` (`ReadOnlyMemory`, `Asn1Any` / `Asn1BitString` / `Asn1Integer`) по возможности **алиасят** эту memory, в том числе от custom `MemoryManager`. Lifetime view удерживается самим `ReadOnlyMemory`; отдельный `Source` не нужен. Мутация входа после decode — UB. Constructed BER (конкатенация сегментов) и materialize (`string` / `BigInteger` / `DateTimeOffset`) аллоцируют. Отвязка — `ToArray` / `Clone` на value-types. Codegen: OCTET STRING → `ReadOnlyMemory<byte>`.

## Reader — фасад + value-type cursor + allocation-free scope

**Причина.** Отдельный mutable buffer-класс упрощает файл `Asn1Reader`, но добавляет heap-объект на каждый top-level decode. Дочерний reader на каждую SEQUENCE делает API локальным, но возвращает аллокации на каждом вложенном PKIX/CMS-типе. Публичный произвольный push окна также позволяет выйти за исходный slice.

**Последствие.** `Asn1Reader` остаётся классом для совместимости с delegate/lazy/reflection, но содержит `Asn1DecodeCursor` как mutable struct. Копия cursor — bookmark для `TryPeekTag` и non-consuming `TryReadOctetString`. Вложенность открывается только `EnterSequence` / `EnterSet` / `EnterExplicit` и возвращает `Asn1ReaderScope`; scope проверяет single-dispose и LIFO. Raw escape hatch один — `ReadAny`; публичных `ReadTlv`, `ReadValue`, `TryReadValue` и `Push` нет. Constructed decoder работает через `ref Asn1DecodeCursor` и не вызывает фасад обратно.

## ANY — `Asn1Any` или open-type + `bindings`

**Причина.** В legacy PKIX `ANY DEFINED BY` встречается постоянно; таблица задаётся overlay. Современные IOC компилятор разрешает в конкретные bindings. Без таблицы OID → тип нельзя честно выбрать; молчаливый пропуск TLV недопустим. `object` / `bool` для NULL — плохой API.

**Последствие.**
- Без `bindings`: полный TLV в `Asn1Any`; `definedBy` информационное.
- С overlay / legacy inline bindings: C# эмитит `Owner_Field` (группировка по CLR-типу, при необходимости `Kind`).
- Свёрнутые IOC-таблицы (`ref.openTypes`): raw на поле + typed `Binding<T>` / table-каталог на контейнере — детали эмиссии только в [csharp-backend.md](../compiler/docs/playbooks/csharp-backend.md).
- Legacy mismatch тега: `options.openType.mismatch` = `soft` (default) / `strict`. Неизвестный ключ → `Unknown`. Modern таблицы всегда отвергают несовместимое содержимое известного ключа.

## Runtime: мягкое чтение неканоничных форм + опции строгости

**Причина.** Реальные PKIX/CMS потоки часто несут TLV, запрещённые строгим DER. Жёсткий reject по умолчанию ломает разбор чужих данных; молчаливый accept без документации и без переключателя — скрытый soft-profile.

**Последствие.** Encode всегда канонический DER. Decode по умолчанию принимает зафиксированные soft-формы; строгий режим — через `Asn1ReaderOptions` (`Default` / `Strict` / точечные профили), не через ветвление только по `Asn1Encoding`. Полный инвентарь soft-accept и флагов — [runtime-api.md](../runtime-csharp/docs/runtime-api.md) (срез — [status.md](status.md) § Runtime); новый soft-accept без записи туда — регрессия процесса.

## Decode — `options.lazy` и `Asn1Lazy<T>`

**Причина.** Глубокие PKIX/CMS деревья (например `Certificate` → `TBSCertificate`) часто нужны лишь частично; полный eager-decode платит за неиспользуемые ветки.

**Последствие.** `options.lazy: true` на поле / typedef / модуле (разрешение: component → TypeExpr → typedef → module) заставляет C# backend обернуть внешнее использование SEQUENCE/SET в `Asn1Lazy<T>`, а SEQUENCE OF/SET OF — в `Asn1Lazy<T[]>`. `Asn1Reader.ReadLazy` захватывает полный TLV без разбора contents; `.Value` вызывает переданный decoder. Encode предпочитает `WriteRaw(EncodedMemory)` при наличии TLV (`HasEncoded`), иначе кодирует `.Value`. Собственный `T.Decode` типа с lazy по-прежнему жадный по своим полям. CHOICE и примитивы не оборачиваются.

## Decode — `options.retainEncoded` и `Asn1Value<T>`

**Причина.** Для хеширования и проверки подписи нужен полный typed decode вместе с исходным полным TLV. Использовать этот TLV как кэш encode небезопасно: вложенный объект или элемент массива может измениться без присваивания `.Value`.

**Последствие.** `options.retainEncoded: true` (та же цепочка разрешения, что у lazy) оборачивает SEQUENCE/SET/OF в allocation-free `Asn1Value<T>`: decode жадный, `OriginalEncoding` сохраняет view исходного TLV, а encode всегда строится из `.Value`. Для созданного приложением значения работает неявное преобразование `T` → `Asn1Value<T>`, при этом `OriginalEncoding` пуст. Обратного неявного преобразования нет. Если одновременно задан `lazy`, побеждает lazy (уже держит TLV). CHOICE и примитивы не оборачиваются.

## C# codegen — typedef-алиасы сворачиваются

**Причина.** Имена вроде `AttributeType ::= OBJECT IDENTIFIER` и `DistinguishedName ::= RDNSequence` порождали sealed-обёртки с единственным `Value`, дублируя CLR-тип без пользы. В C# 10 / net6.0 нет публичных type alias уровня языка (`using` — только внутри файла). То же для `Name ::= CHOICE { rdnSequence RDNSequence }` — CHOICE с одним вариантом на проводе неотличим от альтернативы, а `NameKind` + обёртка только мешают API.

**Последствие.** C# backend не эмитит класс для typedef, чей RHS — не constructed (кроме single-alternative CHOICE) и не `BIT STRING` с `namedBits`: в полях подставляется исходный тип (`string`, `Asn1Any`, `T[]`, …), имя алиаса остаётся в `/// <summary>ASN.1 alias …</summary>`. CHOICE с одним вариантом сворачивается в тип альтернативы (тег CHOICE, если есть, переносится на неё); если и CHOICE, и альтернатива уже с тегами — класс CHOICE сохраняется. Исключение — named BIT STRING: класс с `Asn1BitString Value`, `[Flags]` enum `{Name}Flags` и `ToFlags` / `FromFlags` / свойство `Flags`. IR не меняется.

## C# codegen — однотипный CHOICE → `Kind` + `Value`

**Причина.** `Time` (UTCTime | GeneralizedTime) и строковые CHOICE вроде `DisplayText` / `DirectoryString` порождали N nullable-свойств одного CLR-типа (`DateTimeOffset?` / `string?`). Читать и писать значение неудобно; wire-форма всё равно задаётся `Kind`.

**Последствие.** Если у всех альтернатив одинаковый non-nullable `CsType`, класс имеет одно свойство `Value` и фабрики `From…`, которые выставляют `Kind` + `Value`. `Kind` сохраняется: encode/decode выбирают тег и `Asn1StringForm` / `Asn1TimeForm` по нему, без эвристик. Смешанные CHOICE (`GeneralName` и т.п.) остаются с отдельным свойством на альтернативу.

## Modern PKIX — единый DirectoryString-like CLR для X.520 string CHOICE

**Причина.** В Modern RFC 5912 каждый X.520 typedef (`X520CommonName`, `X520LocalityName`, `X520name`, …) остаётся отдельным ASN.1-типом с одинаковой DirectoryString-формой. Codegen уже схлопывает часть алиасов (O/OU/Title → `X520CommonName`), но отдельные CHOICE-классы и вводящие в заблуждение CLR-имена (OU как `X520CommonName`) остаются. Legacy overlay сводит DN-значения к одному `DirectoryString`.

**Последствие.** Явный options patch (`modern-pkix-cms.patch.json`): `X520name` → `options.csharp.typeName: DirectoryString`, `X520CommonName` / `X520LocalityName` → `options.csharp.aliasOf: X520name`. ASN.1 typedef и SIZE в IR сохраняются; C# эмитит один класс `DirectoryString` (`Kind` + `Value`). Уже IR-ref на CN/L (O/OU/ST/Title/Pseudonym) резолвятся в тот же CLR-тип. Open-type каталог публикует `ValueBinding<DirectoryString>`; когда все members таблицы сводятся к `string` (включая homogeneous CHOICE `.Value` и Printable/IA5), дополнительно эмитится вложенный `AsString` с `ValueBinding<string>` (encode CHOICE через `FromUTF8String`).

## CMS — RFC 5652 §12.1 curated; C# namespace `Asn1Kit.Cms`

**Причина.** При введении legacy CMS конструкции CLASS и parameterized types ещё не поддерживались. RFC 5652 §12.1 (`CryptographicMessageSyntax2004`) — ASN.1:1988 с `ANY DEFINED BY`, как PKIX Explicit88. Полный `CertificateChoices` тянет `AttributeCertificate` / `AttributeCertificateV1` (в legacy-графе этих модулей нет; RFC 5755 импортирует `ContentInfo` из CMS → цикл). Имена `Attribute`, `Time`, `AttributeValue`, `SubjectKeyIdentifier` совпадают с PKIX, но формы другие — общий C# namespace дал бы коллизии типов.

**Последствие.** Фикстура [cms-2004.asn](../compiler/fixtures/asn1/cms-2004.asn): IMPORTS только из PKIX1Explicit88; в `CertificateChoices` оставлены `certificate` / `extendedCertificate` / `other` (ветки `[1]`/`[2]` attr-cert отложены). Golden IR [cms-2004.json](../compiler/fixtures/ir/cms-2004.json) — Explicit + Implicit + CMS; C# CMS в `Asn1Kit.Cms`, PKIX в `Asn1Kit.Pkix` (тот же csproj). Open-type `ContentInfo.content` — [cms-bindings.json](../compiler/fixtures/opentype/cms-bindings.json). IrBuilder резолвит типы/значения **по модулю** (одно имя в разных модулях допустимо).

## DVCS — RFC 3029 и исходный граф ASN.1:1988 без отдельного golden IR

**Причина.** RFC 3029 Appendix E соответствует поддерживаемому профилю, но зависит от CMP, CRMF, OCSP, ESS, S/MIME и CMS. Опубликованный модуль имеет verified errata на импорты `DigestAlgorithmIdentifier` и `GeneralNames`, использует разные написания `CertID` / `ESSCertID` и ссылается на старые X.509/CMS module identifiers. RFC 2510, в свою очередь, оставляет модуль для `CertificationRequest` на стороне реализации.

**Последствие.** В [compiler/fixtures/asn1](../compiler/fixtures/asn1/) хранятся полные модули исходных RFC с комментариями о curated-правках: старые X.509 imports направлены на RFC 5280 Explicit88/Implicit88, CMS — на `CryptographicMessageSyntax2004`, а PKCS#10 собран из RFC 2314. Namespace и продуктовые decode-options задаёт [dvcs.patch.json](../compiler/fixtures/ir/dvcs.patch.json). Промежуточный DVCS IR не коммитится; `*.g.cs` — golden и сверяется генерацией из полного графа. Замена зависимостей на RFC 5911/5912 потребовала бы изменения module identifiers в этом графе, поэтому современная сборка выпускается отдельно.

## PKIX product surface — retainEncoded / lazy cert, без отдельной Bench-сборки

**Причина.** Отдельная сборка `Asn1Kit.Pkix.Bench` дублировала Explicit/Implicit/CMS (~300 KB `.g.cs`) ради performance/verify options (`lazy` на `CertificateChoices.certificate`, `retainEncoded` на TBS/Name/SPKI/Extensions/`signedAttrs`, `AttributeTypeAndValue` как `struct`). Demo и бенчмарки уже требовали этот API; eager golden мешал практическому CMS-пути и раздувал репозиторий.

**Последствие.** Те же field/type options входят в [dvcs.patch.json](../compiler/fixtures/ir/dvcs.patch.json) и применяются к продуктовому `Asn1Kit.Pkix` / `Asn1Kit.Cms`. Отдельные `Asn1Kit.Pkix.Bench`, `cms-2004-bench.*` и `regenerate-bench.ps1` сняты. IR-goldens (`cms-2004.json` и др.) остаются без этих options; они накладываются на generate. CMS-бенчмарки сравнивают Lazy / Lazy+Materialize с BCL/BouncyCastle без отдельного Eager-assembly.

## Современный ASN.1 — частная семантика компилятора, конкретный IR v1

**Причина.** CLASS, WITH SYNTAX, objects/sets и параметризация описывают связи идентификаторов и типов. Если передать их в публичный IR, каждый backend должен повторять ASN.1 resolver. RFC 5912 меняет язык описания PKIX без изменения wire-формы.

**Последствие.** `InformationResolver` между AST и `IrBuilder` разрешает IOC/параметризацию в конкретный IR (bindings, selectors, CONTAINING, extensions). `SpecializationCompactor` сворачивает типы, различающиеся лишь IOC-таблицами, и переносит таблицы на `ref.openTypes`. Лимиты специализаций превращают растущую рекурсию в диагностику. Modern unknown key → raw (без legacy tag-fallback); закрытость наборов и presence пока не исполняются — [status.md](status.md). Эмиссия C# Binding/каталогов — [csharp-backend.md](../compiler/docs/playbooks/csharp-backend.md). Корпус RFC и `Asn1Kit.Modern` отделены от legacy PKIX/CMS/DVCS (разные module identifiers; без эвристик имён).

## C++ — ещё один бэкенд, а не форк фронтенда

**Причина.** Компилятор не знает целевой язык, поэтому второй язык не требует изменений в разборе ASN.1 и в IR.

**Последствие.** Добавляется реализация `ILanguageBackend` в `compiler/` и отдельный C++ runtime в `runtime-cpp/` с теми же правилами BER/DER. Изменения в `Asn1Kit.Compiler` ради C++ — признак утечки слоёв.

## Репозиторий — три каталога продуктов

**Причина.** Компилятор/codegen, C# runtime и будущий C++ runtime развиваются разным темпом и имеют разные зависимости; общий `src/` смешивал слои и фикстуры.

**Последствие.** `compiler/`, `runtime-csharp/`, `runtime-cpp/` — отдельные деревья с локальными README и тестами. Схема IR и сквозная документация остаются в корне. Один `Asn1Kit.sln` собирает оба рабочих каталога.
