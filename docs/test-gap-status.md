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
| G006 | `quality.fuzz` | open | — | Весь исходный критерий. |
| G007 | `der.constructed-strings` | open | — | Требуется решение о политике DER/Strict. |
| G008 | `setof.decode-sort` | open | — | Требуется решение о политике проверки порядка. |
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
| G020 | `cms.external-rfc` | open | — | Весь исходный критерий. |
| G021 | `cms.signed-attrs` | open | — | Весь исходный критерий. |
| G022 | `cms.detached` | open | — | Весь исходный критерий. |
| G023 | `cms.multiple-signers` | open | — | Весь исходный критерий. |
| G024 | `cms.enveloped` | open | — | Весь исходный критерий. |
| G025 | `dvcs.response` | open | — | Весь исходный критерий. |
| G026 | `default.explicit-reject` | open | — | Требуется решение о строгой политике DEFAULT. |
| G027 | `api.zero-copy-callback` | open | — | Весь исходный критерий. |
| G028 | `api.capacity` | open | — | Весь исходный критерий. |
| G029 | `quality.property` | open | — | Весь исходный критерий. |
| G030 | `quality.second-oracle` | open | — | Весь исходный критерий. |
| G031 | `api.redecode-error` | open | — | Весь исходный критерий. |
| G032 | `constraint.enforcement` | open | — | Требует расширения поддержанного профиля constraints. |

## Evidence закрытых и частично закрытых gaps

### G001 — `ber.depth-limit`

- C#: `Ber_IndefiniteScan_EnforcesRecursionDepth` и связанные hostile-BER
  проверки в [RuntimeTests.cs](../runtime-csharp/tests/Asn1Kit.Runtime.Tests/RuntimeTests.cs).
- C++: `HostileBer_IndefiniteScanEnforcesRecursionDepth` и
  `HostileBer_RejectsZeroRecursionDepth` в
  [primitive_codec_tests.cpp](../runtime-cpp/tests/primitive_codec_tests.cpp).
- Реализация: commit `b8f72a8`.

### G002 — `ber.size-limit`

- C#: `ConstructedOctetString_EnforcesMaterializationLimitAndKeepsTryAtomic`,
  `ConstructedBitString_EnforcesMaterializationLimit` и
  `ConstructedString_EnforcesMaterializationLimit` в
  [RuntimeTests.cs](../runtime-csharp/tests/Asn1Kit.Runtime.Tests/RuntimeTests.cs).
- C++: `HostileBer_ConstructedOctetEnforcesLimitsAndKeepsTryAtomic`,
  `HostileBer_PrimitiveOctetDoesNotConsumeMaterializationBudget` и
  `HostileBer_ConstructedBitStringAndStringEnforceMaterializationLimit` в
  [primitive_codec_tests.cpp](../runtime-cpp/tests/primitive_codec_tests.cpp).
- Реализация: commit `b8f72a8`.

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

## Последняя проверка

На 2026-10-16:

- `dotnet test Asn1Kit.sln` — 668 тестов, ошибок нет;
- `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-runtime-cpp.ps1`
  — 261 тест, ошибок нет.
