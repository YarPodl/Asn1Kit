# Состояние пробелов тестового аудита

Этот документ — живой реестр выполнения рекомендаций из исторического снимка
[test-audit.md](test-audit.md), построенного для HEAD
`1c906a3456dd0d60557a60c84531301851002d6e`. Сам снимок и его CSV-артефакты
после реализации тестов не пересчитываются.

## Правила ведения

- `open` — работа по исходному критерию не начиналась.
- `partial` — часть критерия закрыта; незакрытая часть явно записана в колонке
  «Осталось».
- `done` — исходный критерий закрыт исполняемыми тестами во всех указанных
  targets и прошёл обязательные проверки компонента.
- `deferred` — работа осознанно отложена; в колонке «Осталось» обязательна
  причина или ссылка на решение.

Evidence записывается стабильными именами test methods/cases, без номеров строк.
Статус обновляется в том же изменении, где добавляются или удаляются тесты.

`done` не требует повторного анализа `test-audit.md`, пока не произошло хотя бы
одно из событий:

- изменился проверяемый runtime/API-контракт;
- удалено либо переименовано указанное evidence;
- новый аудит явно заменил исходный snapshot.

Для `partial` следующая волна анализирует только колонку «Осталось». Для `open`
исходная постановка берётся из строки с тем же G-ID в
[gaps.csv](test-audit/gaps.csv); повторно доказывать актуальность исторической
находки не требуется.

## Реестр

| Gap | Сценарий | Статус | Покрытие | Осталось |
| --- | --- | --- | --- | --- |
| G001 | `ber.depth-limit` | done | C#, C++ runtime | — |
| G002 | `ber.size-limit` | done | C#, C++ runtime | — |
| G003 | `tlv.truncated` | partial | C#, C++ primitive runtime | Exhaustive prefixes сертификатов, CRL и CMS; проверки Legacy/Modern. |
| G004 | `octet.nested-segments` | done | C#, C++ runtime | — |
| G005 | `bit.segment-padding` | done | C#, C++ runtime | — |
| G006 | `quality.fuzz` | done | C#, C++ runtime + optional libFuzzer | Continuous fuzz в CI не требуется; gate — corpus replay. |
| G007 | `der.constructed-strings` | deferred | soft-read policy | Намеренный soft-accept; отдельный strict-reject не планируется — [решение](decisions.md#constraints-схемы--описательные-без-runtime-enforcement). |
| G008 | `setof.decode-sort` | deferred | soft-read policy | Порядок при decode намеренно не валидируется; encode сортирует DER — [решение](decisions.md#constraints-схемы--описательные-без-runtime-enforcement). |
| G009 | `integer.raw-preserve` | open | — | Весь исходный критерий. |
| G010 | `string.unicode-invalid` | done | C#, C++ runtime | — |
| G011 | `time.invalid-calendar` | done | C#, C++ runtime | — |
| G012 | `oid.extreme-arcs` | done | C#, C++ runtime | — |
| G013 | `pkix.certificate` | open | — | Весь исходный критерий. |
| G014 | `pkix.certificate-variants` | open | — | Весь исходный критерий. |
| G015 | `pkix.crl-variants` | open | — | Весь исходный критерий. |
| G016 | `pkix.extended-extensions` | open | — | Весь исходный критерий. |
| G017 | `pkix.csr` | open | — | Весь исходный критерий. |
| G018 | `pkix.ocsp` | open | — | Весь исходный критерий. |
| G019 | `pkix.attribute-cert` | open | — | Весь исходный критерий. |
| G020 | `cms.external-rfc` | done | — | RFC 4134 корпус + field asserts / DER round-trip. |
| G021 | `cms.signed-attrs` | partial | — | RFC 4134 `4.10`: attrs + retained SET; unsorted permutation oracle остаётся. |
| G022 | `cms.detached` | done | — | RFC 4134 `4.3`. |
| G023 | `cms.multiple-signers` | done | — | RFC 4134 `4.6`. |
| G024 | `cms.enveloped` | open | — | Весь исходный критерий. |
| G025 | `dvcs.response` | open | — | Весь исходный критерий. |
| G026 | `default.explicit-reject` | deferred | soft-read policy | Явно записанный DEFAULT принимается и нормализуется при DER encode; strict-reject не планируется — [решение](decisions.md#constraints-схемы--описательные-без-runtime-enforcement). |
| G027 | `api.zero-copy-callback` | open | — | Весь исходный критерий. |
| G028 | `api.capacity` | open | — | Весь исходный критерий. |
| G029 | `quality.property` | open | — | Весь исходный критерий. |
| G030 | `quality.second-oracle` | open | — | Весь исходный критерий. |
| G031 | `api.redecode-error` | open | — | Весь исходный критерий. |
| G032 | `constraint.enforcement` | deferred | descriptive constraints | Schema constraints намеренно не исполняются при decode/encode; планируется только их публикация в комментариях generated-кода — [решение](decisions.md#constraints-схемы--описательные-без-runtime-enforcement). |

## Evidence закрытых и частично закрытых gaps

### G001 — `ber.depth-limit`

- C#: `Ber_IndefiniteScan_EnforcesRecursionDepth`,
  `Ber_DefiniteSequenceNesting_EnforcesRecursionDepth` и связанные hostile-BER
  проверки в [RuntimeTests.cs](../runtime-csharp/tests/Asn1Kit.Runtime.Tests/RuntimeTests.cs).
- C++: `HostileBer_IndefiniteScanEnforcesRecursionDepth`,
  `HostileBer_DefiniteSequenceNestingEnforcesRecursionDepth` и
  `HostileBer_RejectsZeroRecursionDepth` в
  [primitive_codec_tests.cpp](../runtime-cpp/tests/primitive_codec_tests.cpp).
- Корпус: [hostile-ber.json](../runtime-csharp/fixtures/ber-der/hostile-ber.json)
  (`nestedSequence` / indefinite depth cases).
- Реализация: commit `b8f72a8`; structural depth — MaxRecursionDepth на
  `PushContentsWindow` / `push_contents_window`.

### G002 — `ber.size-limit`

- C#: `ConstructedOctetString_EnforcesMaterializationLimitAndKeepsTryAtomic`,
  `ConstructedOctetString_EnforcesSegmentLimit`,
  `ConstructedBitString_EnforcesMaterializationLimit` и
  `ConstructedString_EnforcesMaterializationLimit` в
  [RuntimeTests.cs](../runtime-csharp/tests/Asn1Kit.Runtime.Tests/RuntimeTests.cs).
- C++: `HostileBer_ConstructedOctetEnforcesLimitsAndKeepsTryAtomic`,
  `HostileBer_ConstructedOctetEnforcesSegmentLimit`,
  `HostileBer_PrimitiveOctetDoesNotConsumeMaterializationBudget` и
  `HostileBer_ConstructedBitStringAndStringEnforceMaterializationLimit` в
  [primitive_codec_tests.cpp](../runtime-cpp/tests/primitive_codec_tests.cpp).
- Реализация: commit `b8f72a8`; segment storm — `MaxConstructedSegments` /
  `max_constructed_segments` (default 4096).

### G003 — `tlv.truncated`

- C#: `EveryProperPrefix_OfRepresentativeTlvs_ThrowsAsn1Exception` в
  [PrimitiveCodecTests.cs](../runtime-csharp/tests/Asn1Kit.Runtime.Tests/PrimitiveCodecTests.cs).
- C++: `EveryProperPrefixOfRepresentativeTlvsThrowsDomainException` в
  [primitive_codec_tests.cpp](../runtime-cpp/tests/primitive_codec_tests.cpp).
- Покрыты high-tag ANY, короткий INTEGER, OCTET STRING с long-form length и
  вложенный indefinite OCTET STRING. Протокольная часть остаётся открытой.

### G004 — `octet.nested-segments`

- Общие cases `octet-ber-nested-definite-indefinite-empty-segments`,
  `octet-rejects-wrong-child-tag`, `octet-rejects-nested-wrong-child-tag` и
  `octet-rejects-truncated-nested-eoc` в
  [octet-string.json](../runtime-csharp/fixtures/ber-der/octet-string.json).
- C++ ownership: `ConstructedOctetMaterializationDetachesFromBorrowedInput` в
  [primitive_codec_tests.cpp](../runtime-cpp/tests/primitive_codec_tests.cpp).

### G005 — `bit.segment-padding`

- Общие success/reject cases с префиксами `bitstring-ber-` и
  `bitstring-rejects-` в
  [bit-string.json](../runtime-csharp/fixtures/ber-der/bit-string.json).

### G006 — `quality.fuzz`

- Optional C++ libFuzzer: [runtime-cpp/fuzz/](../runtime-cpp/fuzz/)
  (`ASN1KIT_BUILD_FUZZ=ON`, Clang, `asn1kit_decode_fuzz` / `asn1kit_fuzz_run`).
- Gate replay: `BoundedDecoderFuzz_CorpusRegressions` в
  [HostileBerCorpusTests.cs](../runtime-csharp/tests/Asn1Kit.Runtime.Tests/HostileBerCorpusTests.cs)
  и [hostile_ber_corpus_tests.cpp](../runtime-cpp/tests/hostile_ber_corpus_tests.cpp)
  по корпусу
  [hostile-ber.json](../runtime-csharp/fixtures/ber-der/hostile-ber.json).
- Continuous fuzz в CI не требуется; новые находки переносятся в корпус.

### G010 — `string.unicode-invalid`

- Общая Unicode-матрица `string-utf8-*`, `string-bmp-*` и
  `string-universal-*` в
  [string.json](../runtime-csharp/fixtures/ber-der/string.json).
- Encode-side tests: `StringEncode_RejectsInvalidUtf8BmpAndLatin1Values` в C# и
  `StringEncodeRejectsInvalidUtf8BmpAndLatin1Values` в C++ — см.
  [PrimitiveCodecTests.cs](../runtime-csharp/tests/Asn1Kit.Runtime.Tests/PrimitiveCodecTests.cs)
  и [primitive_codec_tests.cpp](../runtime-cpp/tests/primitive_codec_tests.cpp).

### G011 — `time.invalid-calendar`

- Общая матрица `time-utc-*` и `time-generalized-*` в
  [time.json](../runtime-csharp/fixtures/ber-der/time.json).
- Rounding tests: `GeneralizedTime_RoundingCarriesAcrossCalendarBoundaries` в
  C# и `GeneralizedTimeRoundingCarriesAcrossCalendarBoundaries` в C++ — см.
  [PrimitiveCodecTests.cs](../runtime-csharp/tests/Asn1Kit.Runtime.Tests/PrimitiveCodecTests.cs)
  и [primitive_codec_tests.cpp](../runtime-cpp/tests/primitive_codec_tests.cpp).

### G012 — `oid.extreme-arcs`

- Общие boundary/reject cases `oid-max-supported-*` и
  `oid-rejects-*-overflow` в
  [oid.json](../runtime-csharp/fixtures/ber-der/oid.json).
- API/count tests: `ObjectIdentifier_EnforcesIntArcDomainAndSupportsManyArcs`
  в C# и `OidEnforcesIntArcDomainAndSupportsManyArcs` в C++ — см.
  [PrimitiveCodecTests.cs](../runtime-csharp/tests/Asn1Kit.Runtime.Tests/PrimitiveCodecTests.cs)
  и [primitive_codec_tests.cpp](../runtime-cpp/tests/primitive_codec_tests.cpp).

### G020 — `cms.external-rfc`

- Фикстуры: [rfc4134/](../runtime-csharp/fixtures/cms/rfc4134/) (`3.2`, `4.2`,
  `4.3`, `4.6`, `4.7`, `4.10`; `expected.json` из `SignedCms` +
  `System.Formats.Asn1`).
- C#: `CmsRfc4134InteropTests` в
  [CmsRfc4134InteropTests.cs](../runtime-csharp/tests/Asn1Kit.Legacy.Tests/CmsRfc4134InteropTests.cs)
  (Modern + Legacy field asserts и byte-identical DER round-trip).
- C++: `GeneratedModernCmsTests` в
  [generated_modern_cms_tests.cpp](../runtime-cpp/tests/generated_modern_cms_tests.cpp).

### G021 — `cms.signed-attrs` (partial)

- RFC 4134 `4.10`: OIDs/`contentType`/`messageDigest`, retained wire TLV
  (`0xA0`) и universal SET contents — `SignedAttrs_4_10_*` /
  `SignedData_4_10_SignedAttrsRoundTrip`.
- Остаток: синтетическая unsorted permutation → канонический DER SET oracle.

### G022 — `cms.detached`

- RFC 4134 `4.3` + `detached-content.bin`: отсутствие `eContent`, round-trip
  оболочки — C# `SignedData_MatchesExternalExpectedAndRoundTrips("4.3.bin")`,
  C++ `SignedData_4_3_DetachedRoundTrip`.

### G023 — `cms.multiple-signers`

- RFC 4134 `4.6` (два signer) и `4.7` (SKI) — C# `MultipleSigners_4_6_*` /
  `SubjectKeyIdentifier_4_7_*`, C++ `SignedData_4_6_*` / `SignedData_4_7_*`.

## Последняя проверка

На 2026-10-20 (после RFC 4134 CMS interop):

- `dotnet test Asn1Kit.sln` — 726 тестов (372 + 71 + 283), ошибок нет;
- `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-runtime-cpp.ps1`
  — 285 тестов, ошибок нет.
