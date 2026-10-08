# Asn1Kit.Modern

Отдельная сборка сгенерированных PKIX/CMS-типов из 35 модулей RFC 5911/5912/6268/8410. Единственная зависимость — `Asn1Kit.Runtime`; namespace каждого модуля начинается с `Asn1Kit.Modern.`. Табличные специализации разделяют один структурный тип, например `AlgorithmIdentifier`; каталоги bindings именуются по object set (`SignatureAlgorithmsParametersBindings`, `CertExtensionsBindings`), а contextual API живёт на контейнере. Структурно разные `SIGNED` представлены `Signed<T>` и именованными производными. Имена классов не содержат хешей; CLASS и шаблоны не входят в API сборки.

Исходный PKIX/CMS/DVCS остаётся в `Asn1Kit.Pkix`. Современные module identifiers отличаются от импортов DVCS, поэтому эти сборки используются независимо. Необъявленные algorithm sets `{...}` остаются открытыми пустыми таблицами; библиотека не подставляет в них все известные алгоритмы. Для практического типизированного API компилируйте собственный модуль, специализирующий шаблон выбранным object set.

CONTAINING представлен `Asn1Contained<T>` с исходными `Contents` и, при известном типе, `HasValue`/`Value`. Открытые TLV имеют фабрики `From…`/`FromUnknown`; неизвестный OID всегда остаётся raw. Составные DEFAULT создаются отдельно для каждого объекта; encoder пропускает структурно равные значения. Расширяемые SEQUENCE/SET пропускают неизвестные additions при decode и не пишут их при encode; у расширяемого CHOICE неизвестная альтернатива сохраняется целиком.

Для известных bindings сырых open types конечный тип специализации публикуется непосредственно в table-scoped descriptor: например, `CertExtensionsBinding<AuthorityKeyIdentifier>` и `SignedAttributesSetBinding<ReadOnlyMemory<byte>[]>`. Descriptor содержит key, decoder и encoder. Методы `Set…(binding, value)` записывают новое значение в существующий raw-контейнер и сохраняют его метаданные; новое содержимое кодируется в DER. Отдельные классы `AuthorityKeyIdentifierExtension` / `MessageDigestAttribute` и преобразования `To…` / `TryFrom…` не генерируются. Примитивы используют общие `Asn1Codecs` runtime; codec непримитивной wire-специализации эмитится один раз на модуль, без отдельного `BindingCodec` для каждого ключа.

Bindings с типом `NULL`, включая алиасы, доступны как `Binding<Asn1Null>`; отсутствие параметров и закодированный `NULL` различаются через основной descriptor-overload. Для `NULL` вызывайте generic setter с `Asn1Null.Value`: `algorithm.SetParameters(SignatureAlgorithmsParametersBindings.SaRsaWithSHA1, Asn1Null.Value)`. Именованные convenience-методы не генерируются.

```csharp
using Asn1Kit.Modern.PKIX1Explicit2009;
using Asn1Kit.Modern.CryptographicMessageSyntax2009;

if (certificate.ToBeSigned.Extensions.TryGet(
        CertExtensionsBindings.AuthorityKeyIdentifier, out var aki))
    Console.WriteLine(aki.KeyIdentifier);

if (signer.SignedAttrs?.Value.TryGet(
        SignedAttributesSetBindings.MessageDigest, out var digest) == true)
    Console.WriteLine(digest.Length);

if (certificate.ToBeSigned.Signature.TryDecodeParameters(
        SignatureAlgorithmsParametersBindings.SaRsaSSAPSS, out var pss))
    Console.WriteLine(pss.SaltLength);

var extension = new Asn1Kit.Modern.PKIXCommonTypes2009.Extension { Critical = false };
extension.SetExtnValue(
    CertExtensionsBindings.AuthorityKeyIdentifier,
    new Asn1Kit.Modern.PKIX1Implicit2009.AuthorityKeyIdentifier
    {
        KeyIdentifier = keyIdentifier
    });
```

Descriptor-каталоги `SignedAttributesSetBindings` и `UnsignedAttributesBindings` используют несовместимые типы таблиц. Пользовательский полный binding создаётся через ctor `ParametersBinding<T>` / `ExtnValueBinding<T>` / … `(key, decoder, encoder)` или `(key, codec)`; `DecoderBinding<T>` — для contextual чтения неоднозначно сплющенных ветвей `CHOICE`. Тип результата выводится из descriptor. Например, параметры алгоритма читаются через `algorithm.TryDecodeParameters(SignatureAlgorithmsParametersBindings.SaRsaWithSHA1, out var value)`, а строковая ветка DN — через `TryDecodeValue(SupportedAttributesValueBindings.X520CommonNameStringValue, out var value)` или owner `TryGetSubject`/`TryGetIssuer` для вложенного DN. Отсутствующий key даёт `false`, несколько совпадений и повреждённое известное значение вызывают `Asn1Exception`. Плоский OF — `Carrier[]?.TryGet`; требования к количеству значений проверяет приложение. Selector предка требует явного контекста. Dispatch по `typeof(T)` в open-type API не используется.

Происхождение и исправления: [корпус ASN.1](../../../compiler/fixtures/asn1/modern/README.md). Профиль и ограничения: [docs/status.md](../../../docs/status.md). Криптографическая проверка и полная валидация ASN.1 constraints в сборку не входят.

Options patch [modern-pkix-cms.patch.json](../../../compiler/fixtures/ir/modern-pkix-cms.patch.json) сохраняет исходные TLV для PKIX `Certificate.toBeSigned`, `TBSCertificate.issuer/subject/subjectPublicKeyInfo` и CMS `CertificateChoices.certificate` (`-2009`), `IssuerAndSerialNumber.issuer` и `SignerInfo.signedAttrs` (`-2009` и `-2010`).

Каноническая регенерация из корня репозитория:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/regenerate-modern.ps1
dotnet test Asn1Kit.sln
```

`*.g.cs` и соответствующий IR golden вручную не правятся. `ModernRfcTests` проверяет все generated-файлы через Roslyn, внешние сертификаты, opaque CMS data и составные DEFAULT RSA-PSS.
