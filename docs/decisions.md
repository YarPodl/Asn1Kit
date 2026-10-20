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

## ANY — один Binding-путь (overlay и IOC)

**Причина.** В legacy PKIX `ANY DEFINED BY` встречается постоянно; таблица задаётся overlay. Современные IOC компилятор разрешает в конкретные bindings. Dual-path (`Owner_Field` vs `Binding<T>`) дублировал API и раздувал Alloc на legacy decode. Без таблицы OID → тип нельзя честно выбрать; молчаливый пропуск TLV недопустим.

**Последствие.**
- Без таблицы: полный TLV в `Asn1Any`; `definedBy` информационное.
- Overlay sidecar и IOC сходятся в одну IR-форму: `any.selector` (+ при orphan — `bindings`/`tableExtensible` на теле; при refs — таблицы на `ref.openTypes`). Inline `any.bindings` после normalize не является codegen-контрактом.
- C# всегда: поле `Asn1Any` + typed `Binding<T>` / table-каталог / `TryDecode`·`Set`·`TryGet` / при однородном leaf — `AsString` — детали в [csharp-backend.md](../compiler/docs/playbooks/csharp-backend.md).
- Decode: неизвестный ключ → raw; известный mismatch → `Asn1Exception`. `options.openType.mismatch` и universal-tag fallback для open types сняты (опция deprecated/no-op).

## Runtime: мягкое чтение неканоничных форм + опции строгости

**Причина.** Реальные PKIX/CMS потоки часто несут TLV, запрещённые строгим DER. Жёсткий reject по умолчанию ломает разбор чужих данных; молчаливый accept без документации и без переключателя — скрытый soft-profile.

**Последствие.** Encode всегда канонический DER. Decode по умолчанию принимает зафиксированные soft-формы; строгий режим — через `Asn1ReaderOptions` (`Default` / `Strict` / точечные профили), не через ветвление только по `Asn1Encoding`. Полный инвентарь soft-accept и флагов — [runtime-api.md](../runtime-csharp/docs/runtime-api.md) (срез — [status.md](status.md) § Runtime); новый soft-accept без записи туда — регрессия процесса.

## Constraints схемы — описательные, без runtime enforcement

**Причина.** Реальные PKIX/CMS-объекты встречаются с нарушениями декларативных ограничений ASN.1-модуля. Автоматический отказ по `SIZE`, диапазону значения, `WITH COMPONENTS`, presence или закрытости object set сделал бы такие структурно корректные BER-объекты нечитаемыми. Тот же tolerant-контракт нужен при построении значения: generated API не должен скрыто превращаться в валидатор прикладного профиля.

**Решение.** Schema constraints сохраняются в IR как описательные метаданные, но generated decode и encode их не исполняют. Это относится к `constraint.size`, `constraint.value`, `constraint.unsupported`, `WITH COMPONENTS`, `PRESENT` / `ABSENT` / constraint-`OPTIONAL`, `FROM`, `PATTERN` и признаку закрытого object set. Неизвестный ключ любой open-type таблицы остаётся raw; известный ключ с payload, который нельзя декодировать объявленным типом, по-прежнему вызывает ошибку. Эмиссия constraints в комментарии generated C# и C++ явно отмечает, что они не проверяются.

**Граница решения.** Wire-корректность и ресурсная безопасность не являются schema constraints: runtime продолжает проверять теги, длины, границы входа, EOC, обязательное полное потребление typed value, корректность Unicode/BIT STRING/OID/календаря, числовой overflow и hostile-input limits (глубина indefinite/structural nesting, byte-бюджет и число leaf-сегментов constructed materialize; см. status § Runtime). Запись остаётся каноническим DER. При чтении намеренно не планируется отдельный reject для constructed OCTET/BIT/string, порядка `SET OF` и явно записанного `DEFAULT`: корректный поддержанный BER принимается, а повторная запись канонизирует значение.

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

**Последствие.** Фикстура [cms-2004.asn](../compiler/fixtures/asn1/cms-2004.asn): IMPORTS только из PKIX1Explicit88; в `CertificateChoices` оставлены `certificate` / `extendedCertificate` / `other` (ветки `[1]`/`[2]` attr-cert отложены). Golden IR [cms-2004.json](../compiler/fixtures/ir/cms-2004.json) — Explicit + Implicit + CMS; C# CMS в `Asn1Kit.Legacy.Cms`, PKIX в `Asn1Kit.Legacy.Pkix` (сборка `Asn1Kit.Legacy`). Open-type `ContentInfo.content` — [cms-bindings.json](../compiler/fixtures/opentype/cms-bindings.json). IrBuilder резолвит типы/значения **по модулю** (одно имя в разных модулях допустимо).

## TSP — RFC 3161 Appendix C + ASN-патч в Modern

**Причина.** В RFC 5911/5912 нет IOC-модуля TSP; полный rewrite Appendix C «в стиле 5912» дублирует wire-контракт и размывает происхождение. As-published IMPORTS ссылаются на старые module OID и `CryptographicMessageSyntax {cms(1)}`, которых нет в корпусе Modern.

**Последствие.** Источник — [sources/PKIXTSP.rfc3161.asn](../compiler/fixtures/asn1/modern/sources/PKIXTSP.rfc3161.asn); курированный [PKIXTSP-2009.asn](../compiler/fixtures/asn1/modern/PKIXTSP-2009.asn) собирает [patch-pkixtsp-modern.ps1](../scripts/patch-pkixtsp-modern.ps1): IMPORTS на `PKIX-CommonTypes-2009` / `PKIX1Implicit-2009` / `CryptographicMessageSyntax-2009` / `PKIXCMP-2009`, `AlgorithmIdentifier` → `DigestAlgorithmIdentifier`, `Extensions` → `Extensions{{...}}`, починка оборванных RFC-комментариев. Module OID `id-mod-tsp(13)` и `IMPLICIT TAGS` сохранены. Namespace — `Asn1Kit.Modern.PKIXTSP2009`. `eContent` с `id-ct-TSTInfo` остаётся opaque в CMS `ContentSet` (как у ESS `ct-receipt`); typed decode — `TSTInfo.Decode` по октетам.

## DVCS — RFC 3029 и исходный граф ASN.1:1988 без отдельного golden IR

**Причина.** RFC 3029 Appendix E соответствует поддерживаемому профилю, но зависит от CMP, CRMF, OCSP, ESS, S/MIME и CMS. Опубликованный модуль имеет verified errata на импорты `DigestAlgorithmIdentifier` и `GeneralNames`, использует разные написания `CertID` / `ESSCertID` и ссылается на старые X.509/CMS module identifiers. RFC 2510, в свою очередь, оставляет модуль для `CertificationRequest` на стороне реализации.

**Последствие.** В [compiler/fixtures/asn1](../compiler/fixtures/asn1/) хранятся полные модули исходных RFC с комментариями о curated-правках: старые X.509 imports направлены на RFC 5280 Explicit88/Implicit88, CMS — на `CryptographicMessageSyntax2004`, а PKCS#10 собран из RFC 2314. Namespace и продуктовые decode-options задаёт [dvcs.patch.json](../compiler/fixtures/ir/dvcs.patch.json). Промежуточный DVCS IR не коммитится; `*.g.cs` — golden и сверяется генерацией из полного графа. Замена зависимостей на RFC 5911/5912 потребовала бы изменения module identifiers в этом графе, поэтому современная сборка выпускается отдельно.

## PKIX product surface — retainEncoded / lazy cert, без отдельной Bench-сборки

**Причина.** Отдельная сборка `Asn1Kit.Pkix.Bench` дублировала Explicit/Implicit/CMS (~300 KB `.g.cs`) ради performance/verify options (`lazy` на `CertificateChoices.certificate`, `retainEncoded` на TBS/Name/SPKI/Extensions/`signedAttrs`, `AttributeTypeAndValue` как `struct`). Demo и бенчмарки уже требовали этот API; eager golden мешал практическому CMS-пути и раздувал репозиторий.

**Последствие.** Те же field/type options входят в [dvcs.patch.json](../compiler/fixtures/ir/dvcs.patch.json) и применяются к `Asn1Kit.Legacy.Pkix` / `Asn1Kit.Legacy.Cms`. Отдельные `Asn1Kit.Pkix.Bench`, `cms-2004-bench.*` и `regenerate-bench.ps1` сняты. IR-goldens (`cms-2004.json` и др.) остаются без этих options; они накладываются на generate. CMS-бенчмарки сравнивают Lazy / Lazy+Materialize с BCL/BouncyCastle без отдельного Eager-assembly.

## Современный ASN.1 — частная семантика компилятора, конкретный IR v1

**Причина.** CLASS, WITH SYNTAX, objects/sets и параметризация описывают связи идентификаторов и типов. Если передать их в публичный IR, каждый backend должен повторять ASN.1 resolver. RFC 5912 меняет язык описания PKIX без изменения wire-формы.

**Последствие.** `InformationResolver` между AST и `IrBuilder` разрешает IOC/параметризацию в конкретный IR (bindings, selectors, CONTAINING, extensions). `SpecializationCompactor` сворачивает типы, различающиеся лишь IOC-таблицами, и переносит таблицы на `ref.openTypes`. `InlineCompositeDeduper` после compact схлопывает inline SEQUENCE/SET/multi-CHOICE на существующий typedef с тем же wire-shape и теми же `options` (fingerprint без `selector`/`bindings`/`table`/`openTypes`); таблицы уходят на `ref.openTypes`, тело typedef сохраняет local selector. Так `SIGNED.algorithmIdentifier` и прочие анонимные клоны становятся общим `AlgorithmIdentifier`. Лимиты специализаций превращают растущую рекурсию в диагностику. Modern unknown key → raw (без legacy tag-fallback); закрытость наборов и presence пока не исполняются — [status.md](status.md). Эмиссия C# Binding/каталогов — [csharp-backend.md](../compiler/docs/playbooks/csharp-backend.md). Корпус RFC и `Asn1Kit.Modern` отделены от legacy PKIX/CMS/DVCS (разные module identifiers; без эвристик имён).

## C++ — ещё один бэкенд, а не форк фронтенда

**Причина.** Компилятор не знает целевой язык, поэтому второй язык не требует изменений в разборе ASN.1 и в IR.

**Последствие.** Добавляется реализация `ILanguageBackend` в `compiler/` и отдельный C++ runtime в `runtime-cpp/` с теми же правилами BER/DER. Изменения в `Asn1Kit.Compiler` ради C++ — признак утечки слоёв.

## C++ codegen — cycle headers только для SCC, deps через Module.types.hpp

**Причина.** Амальгама «SCC + транзитивный конус deps» и рёбра от `AnyType.Bindings` раздували `cycle_*.hpp` (десятки листьев вроде `AlgorithmIdentifier`). Open-type payload не нужен для complete type в C++ (остаётся `any` / descriptors).

**Последствие.** Граф deps — только layout. `cycle_*.hpp` содержит только членов нетривиального SCC и `#include`-ит `Module.types.hpp` / `Module.hpp` / другие cycle по DAG конденсации, но не facade с cycle-includes. `Module.types.hpp` (нециклические типы + OID) создаётся только когда модулю нужен facade (owned cycle / post / open-type); иначе всё в одном `Module.hpp`. Facade не дублирует OID, если они уже в `*.types.hpp`. Peer `#include` в types/facade **откладываются** до первого owning-типа; типы с reentrant peer (include-triangle CMS→AttrCert→ESS→CMS) и их in-module потомки эмитятся после остальных, чтобы локальные типы (например `IssuerAndSerialNumber`) были полны до реentrant include.

## C++ open types — functional view как primary API

**Причина.** Плоские синтетические namespace каталогов требовали знания внутреннего алгоритма именования. Публичный registry-путь `Type::open_types::…` дублировал functional view и раздувал consumer-поверхность.

**Решение.** Основной API — объектный functional view: `info.content_set().ct_signed_data()`, mutable `info.content_set().ct_signed_data(value)`, создание `ContentInfo::make_content_set().ct_signed_data(value)`, decode с временного carrier `ContentInfo::decode(r).content_set().ct_signed_data()`. Descriptor-каталоги живут непосредственно в `namespace detail` модуля; промежуточная emitted registry-иерархия и `using open_types` на model-типе отсутствуют. View ссылается на descriptor как NTTP. Именованный entry — forwarding к `open_function_view`; mutable/const/owned/factory режимы используют один generated view-тип. Rvalue-accessor владеет carrier (`open_owned_target`) и поддерживает только decode; вложенный переход с rvalue переносит carrier в дочерний view. Contextual view read-only, а повторные применения одной таблицы к разным routes различаются семантическим route-суффиксом. Пользовательские keys — `open_binding<T>(key)` + `try_decode`/`set`/`create` с `default_open_context`. Безымянные таблицы — под `local`. Старые synthetic namespaces и публичный `open_types` удалены без compatibility aliases.

**Последствие.** Accessors объявляются на типе, определяются out-of-line после complete view в facade — без `conditional_t`/`OpenView`-хака. Bound view хранит указатель, owned — значение, factory пуст. Generated forwarding-макрос локален facade и удаляется после view definitions; entry-имена разрешают коллизии со служебными членами view. Payload не добавляет layout-рёбра в `*.types.hpp` и cycle-amalgam; unknown key / malformed known value сохраняют прежнюю семантику.

## C++ codegen — глубокий debug-формат в отдельных `Module.format.hpp`

**Причина.** Плоский `Asn1Formatting` в C# не подходит для отладки нативных структур: нужен рекурсивный многострочный дамп с отступами без промежуточной `std::string`.

**Решение.** Публичная точка входа — потоковая и ADL-дружественная: `template<class T> void to_string(const T&, std::ostream&)` в `runtime-cpp/include/asn1kit/format.hpp` вызывает `to_string(value, out, 0)`; конкретные перегрузки (`int32_t`…`asn1kit::integer`, `bytes`, `bit_string`, `oid`, `utc_date_time`, `any`, `null_value`, `std::optional`, `std::vector`, `value<T>`, `lazy<T>`) — header-only там же. Всеядного primary-шаблона `to_string(const T&, std::ostream&, int)` нет: он уводил бы ADL-хук в бесконечную рекурсию. Генератор эмитит `{Module}.format.hpp`: forward-объявления `to_string` своих типов, `#include` format-заголовков owning-модулей всех форматированных layout-ссылок, затем `inline`-определения. Объявления до пиров разрывают взаимную рекурсию циклов через include-guard.

**Последствие.** Тела пишутся всегда (сейчас без `options.cpp.*`); named INTEGER-структуры констант формат не получают, `generate: false` уважается. Кодек не дублируется — используется только API runtime. Точка расширения для пользовательских типов: перегрузка `to_string(const T&, std::ostream&, int)` в namespace типа.

## C++ codegen — namespace-квалификация cross-module имён

**Причина.** Синтезированные владельцы вложенных типов (например `<Owner>_Item` в `SEQUENCE OF`), прежние type-specific ENUMERATED-кодеки (`encode_<Type>` / `decode_<Type>`) и имена типов, полученные разворачиванием сворачиваемого alias (`options.aliasOf`), могли эмититься без namespace-квалификации, когда определение живёт в другом модуле. В modern PKIX из-за этого не собирались `value_binding<CertId>` / `value_binding<CertRequest>` и `extnValue_binding<std::vector<PolicyMappings_Item>>` в OID-каталоге `PKIXCommonTypes2009.hpp`, а также старые `decode_CRLReason` / `encode_CRLReason` в OCSP / EnrollmentMessageSyntax (тип `CRLReason` определён в `PKIX1Implicit2009`).

**Решение.** Общий хелпер `QualifyCrossModule(emitting, owning, name)` добавляет `::<namespace>::` к имени, когда namespace владельца отличается от namespace эмиттера. Он применяется к владельцу OF-элемента (`ResolveOfItemNaming`). `NamedTypeName` квалифицирует определение, если его namespace не совпадает ни с резолвящим, ни с эмитирующим модулем: после разворачивания alias эти контексты расходятся, и одного из них недостаточно. После перехода ENUMERATED на единый ADL-контракт `encode_enumerated` / `decode_enumerated` отдельный `EnumeratedCodecName` удалён: encode выбирает перегрузку по значению, а decode передаёт квалифицированный `std::type_identity<T>`.

**Последствие.** Устранены unqualified cross-module ссылки в modern golden (`PKIXCommonTypes2009.hpp`, `OCSP2009.types.hpp`, `EnrollmentMessageSyntax2009.types.hpp`). Регрессия закрыта тестом `CppCodegenTests.ModernCrossModuleNamesAreQualified`, включая qualified type token для ENUMERATED.

## C++ runtime — C++20, `bytes` = span + keep-alive, Conan только для тестов

**Причина.** Нужен нативный runtime с тем же soft-profile и hex-матрицей, без runtime-зависимостей и без копирования фикстур. `std::span` сам по себе не удерживает backing store.

**Последствие.** Стандарт C++20; публичный `asn1kit::bytes` (span + type-erased keep-alive); библиотека без внешних deps; gtest и nlohmann_json — Conan 2 только для `tests/` (или FetchContent). Windows и Linux равноправны (MSVC/Clang/GCC). Soft-read и constructed string/BIT/OCTET под DER совпадают с эталонным runtime; фикстуры — `runtime-csharp/fixtures/ber-der`. Публичные SEQUENCE/SET scopes — move-only RAII + рекомендуемые `with_*` (auto empty-check на success). Codegen C++ — отдельный крупный шаг.

## Репозиторий — три каталога продуктов

**Причина.** Компилятор/codegen, C# runtime и C++ runtime развиваются разным темпом и имеют разные зависимости; общий `src/` смешивал слои и фикстуры.

**Последствие.** `compiler/`, `runtime-csharp/`, `runtime-cpp/` — отдельные деревья с локальными README и тестами. Схема IR и сквозная документация остаются в корне. `Asn1Kit.sln` собирает .NET-каталоги; C++ — через CMake в `runtime-cpp/`.

## Общая опция aliasOf для генераторов

**Решение.** Объединение typedef задаётся через `options.aliasOf` для C# и C++. Языковые имена остаются в `options.csharp.typeName` / `options.cpp.typeName`. Modern X.520 использует единый `DirectoryString` в обоих бэкендах.

**Совместимость.** Старые языковые aliasOf сохраняются для IR v1; при совместном указании с общей опцией значения должны совпадать. Проверки целей, циклов и конфликтов с typeName едины. Исходные определения и ограничения ASN.1 в IR не переписываются.
