# Asn1Kit.Modern

Отдельная сборка сгенерированных PKIX/CMS-типов из 35 модулей RFC 5911/5912/6268/8410. Единственная зависимость — `Asn1Kit.Runtime`; namespace каждого модуля начинается с `Asn1Kit.Modern.`. Табличные специализации разделяют один структурный тип, например `AlgorithmIdentifier`; у владельцев прямых ссылок есть методы чтения и записи параметров по своей таблице. Структурно разные `SIGNED` представлены `Signed<T>` и именованными производными. Имена классов не содержат хешей; CLASS и шаблоны не входят в API сборки.

Исходный PKIX/CMS/DVCS остаётся в `Asn1Kit.Pkix`. Современные module identifiers отличаются от импортов DVCS, поэтому эти сборки используются независимо. Необъявленные algorithm sets `{...}` остаются открытыми пустыми таблицами; библиотека не подставляет в них все известные алгоритмы. Для практического типизированного API компилируйте собственный модуль, специализирующий шаблон выбранным object set.

CONTAINING представлен `Asn1Contained<T>` с исходными `Contents` и, при известном типе, `HasValue`/`Value`. Открытые TLV имеют фабрики `From…`/`FromUnknown`; неизвестный OID всегда остаётся raw. Составные DEFAULT создаются отдельно для каждого объекта; encoder пропускает структурно равные значения. Расширяемые типы имеют `UnknownExtensions`; у расширяемого CHOICE неизвестная альтернатива сохраняется целиком.

Происхождение и исправления: [корпус ASN.1](../../../compiler/fixtures/asn1/modern/README.md). Профиль и ограничения: [docs/status.md](../../../docs/status.md). Криптографическая проверка и полная валидация ASN.1 constraints в сборку не входят.

Options patch [modern-pkix-cms.patch.json](../../../compiler/fixtures/ir/modern-pkix-cms.patch.json) сохраняет исходные TLV для PKIX `Certificate.toBeSigned`, `TBSCertificate.issuer/subject/subjectPublicKeyInfo` и CMS `CertificateChoices.certificate` (`-2009`), `IssuerAndSerialNumber.issuer` и `SignerInfo.signedAttrs` (`-2009` и `-2010`).

Каноническая регенерация из корня репозитория:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/regenerate-modern.ps1
dotnet test Asn1Kit.sln
```

`*.g.cs` и соответствующий IR golden вручную не правятся. `ModernRfcTests` проверяет все generated-файлы через Roslyn, внешние сертификаты, opaque CMS data и составные DEFAULT RSA-PSS.
