# Современный корпус PKIX/CMS

**27** ASN.1-модулей из [RFC 5911](https://www.rfc-editor.org/rfc/rfc5911), [RFC 5912](https://www.rfc-editor.org/rfc/rfc5912), [RFC 6268](https://www.rfc-editor.org/rfc/rfc6268), [RFC 8410 §9](https://www.rfc-editor.org/rfc/rfc8410#section-9), [RFC 9215](https://www.rfc-editor.org/rfc/rfc9215) (GOST R 34.10-2012 / RuStrongCerts) и curated [RFC 3161](https://www.rfc-editor.org/rfc/rfc3161) TSP (`PKIXTSP-2009`). Один модуль — один `.asn`; исходные CLASS, WITH SYNTAX, object sets, параметризация, imports и constraints сохранены. Поддержка не зависит от специальных имён PKIX-типов или алгоритмов.

TSP не входит в RFC 5911/5912: исходник Appendix C лежит в [sources/PKIXTSP.rfc3161.asn](sources/PKIXTSP.rfc3161.asn); корпусный файл получает [scripts/patch-pkixtsp-modern.ps1](../../../../scripts/patch-pkixtsp-modern.ps1) (IMPORTS на Modern PKIX/CMS/CMP, `DigestAlgorithmIdentifier`, `Extensions{{...}}`).

Извлечение удаляет оформление страниц RFC (заголовки, номера страниц, form feed). При совпадении имени модуля приоритет имеют RFC 5912, затем 5911, 6268, 8410, 9215: общий `AlgorithmInformation-2009` хранится один раз. Из корпуса исключены CMS-2010-стек, нишевые расширения (`WLANCertExtn-2010`, `IPAddrAndASCertExtn-2010`, `BinarySigningTimeModule-2010`, `HMAC-2010`, `SCVP-2009`) и `CMS-AES-CCM-and-AES-GCM-2009`; product CMS — `CryptographicMessageSyntax-2009`. Skip задаёт [import-modern-rfc.ps1](../../../../scripts/import-modern-rfc.ps1). Иллюстративная незавершённая заготовка `ModuleNumbers` из RFC 6268 не является полным модулем и не включается. Модули ГОСТ из RFC 9215 — 1988-стиль без `IMPORTS` на `AlgorithmInformation-2009` (OID и параметры ключей; не CLASS-регистрация 5912).

## Исправления опубликованных исходников

Исправления применяет [scripts/import-modern-rfc.ps1](../../../../scripts/import-modern-rfc.ps1), а не специальные правила компилятора:

- Verified [EID 2612](https://www.rfc-editor.org/errata/eid2612) / [EID 3130](https://www.rfc-editor.org/errata/eid3130): CONTENT-TYPE имеет optional `&Type` и шаблон `[TYPE &Type] IDENTIFIED BY &id`; `ct-Data` не объявляет ASN.1-тип. Для типизированных content objects добавляется литерал TYPE. RFC 6268 уже содержит исправленную форму.
- Verified [EID 3128](https://www.rfc-editor.org/errata/eid3128): ERS импортирует `CryptographicMessageSyntax-2009` вместо старого `CryptographicMessageSyntax2004`.
- Verified [EID 3259](https://www.rfc-editor.org/errata/eid3259): OCSP импортирует `CRLReason` из `PKIX1Implicit-2009`; ошибочный локальный `CRLReason ::= INTEGER` удаляется. Wire-тип — ENUMERATED.
- Локальное исправление двух ошибок копирования в `CMS-AES-CCM-and-AES-GCM-2009`: `cea-aes192-GCM` и `cea-aes256-GCM` используют соответствующие `id-aes192-GCM` / `id-aes256-GCM`, а не `id-aes128-GCM`. Различные OID определены в [RFC 5084 §4](https://www.rfc-editor.org/rfc/rfc5084#section-4). Это исправление не обозначается как зарегистрированная errata.
- RFC 9215 `GostR3410-2012-PublicKey`: `SIZE (64 | 128)` вне профиля constraint Asn1Kit; typedef остаётся `OCTET STRING`. Отдельные `SIZE (64)` / `SIZE (128)` алиасы сохраняются.

Остальные errata автоматически не применяются. ERS содержит написание `PKIX-CommonTypes` без суффикса `-2009`, но импорт указывает точный module OID; компилятор разрешает его по этой идентичности. Имя в исходнике не переписывается.

Текст RFC и извлечённые code components распространяются по условиям IETF Trust; уведомление присутствует в каждом `.asn`, полный Simplified BSD license — [LICENSE.txt](LICENSE.txt).

## Воспроизведение

Из корня репозитория, .NET SDK 6.0.402:

```powershell
New-Item -ItemType Directory -Force .cache/rfc | Out-Null
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
foreach ($number in @(5911, 5912, 6268, 8410, 9215)) {
    Invoke-WebRequest -UseBasicParsing "https://www.rfc-editor.org/rfc/rfc$number.txt" -OutFile ".cache/rfc/rfc$number.txt"
}
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/import-modern-rfc.ps1 -InputDirectory .cache/rfc
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/patch-pkixtsp-modern.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/regenerate-modern.ps1
dotnet test Asn1Kit.sln
```

Регенерация IR/C# использует уже сохранённые `.asn` и не требует сети. `patch-pkixtsp-modern.ps1` обновляет только `PKIXTSP-2009.asn` из `sources/PKIXTSP.rfc3161.asn`. Порядок входных файлов — ordinal; namespace назначается отдельно каждому модулю. JSON golden и `*.g.cs` вручную не правятся. `ModernRfcTests` читает весь корпус, сверяет артефакты и собирает C# через Roslyn. Поддержанный профиль и отложенная проверка constraints — [docs/status.md](../../../../docs/status.md).
