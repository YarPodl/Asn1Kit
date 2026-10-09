# Демонстрация проверки CMS SignedData

Один консольный проект проверяет attached `SignedData` двумя способами: через `Asn1Kit.Pkix` по умолчанию и через `Asn1Kit.Modern` с флагом `--modern`. Реализации находятся в отдельных файлах `CmsSignedDataInspector.cs` и `CmsModernSignedDataInspector.cs`. Запуск из корня репозитория:

```powershell
dotnet run --project runtime-csharp/examples/Asn1Kit.Cms.Demo -- runtime-csharp/fixtures/cms/attached-signeddata.p7m
dotnet run --project runtime-csharp/examples/Asn1Kit.Cms.Demo -- --modern runtime-csharp/fixtures/cms/attached-signeddata.p7m
# Проверка цепочки и RSA/SHA-256: корень передаётся отдельно; промежуточные сертификаты необязательны.
dotnet run --project runtime-csharp/examples/Asn1Kit.Cms.Demo -- --trusted-root root.cer --certificate intermediate.cer message.p7m
dotnet run --project runtime-csharp/examples/Asn1Kit.Cms.Demo -- --modern --trusted-root root.cer message.p7m
```

Оба режима проверяют BER-структуру и отсутствие хвоста, `ContentInfo.contentType`, наличие `eContent` и подписантов, присутствие алгоритма дайджеста подписанта в `SignedData.digestAlgorithms` и связь подписанта с вложенным сертификатом по issuer/serial либо Subject Key Identifier. Если есть `signedAttrs`, проверяются `contentType` и `messageDigest`: каждое значение должно присутствовать ровно один раз, а `contentType` — совпадать с типом вложенного содержимого. Для проверки подписи `Asn1Any.FromTagAndContents` меняет только внешний тег сохранённого TLV `[0]` на `SET`; при отсутствии `signedAttrs` используются байты содержимого. Повторного кодирования атрибутов из decoded-модели нет. Семантика других атрибутов не проверяется.

Modern-инспектор читает AKI/SKI через `Extensions.TryGet(CertExtensionsBindings.…)` и CMS-атрибуты через `SignedAttrs?.Value.TryGet(SignedAttributesSetBindings.…) == true`. Descriptor сразу возвращает конечный тип специализации и содержит codec для записи raw-поля; отдельные per-binding обёртки и named convenience не нужны. Signed и unsigned attrs имеют разные descriptor-типы. Проверки структуры AKI, обязательности и количества значений CMS остаются в приложении.

Для принятого подписанта демо печатает `Issuer` и `Subject` его сертификата в форме [RFC 4514](https://www.rfc-editor.org/rfc/rfc4514.html): RDN идут в обратном порядке через запятую, атрибуты одного RDN — через `+`, специальные символы экранируются. Неизвестные OID и значения без строкового представления выводятся как OID и `#` с шестнадцатеричной записью TLV значения (`Asn1Any`). Строковые атрибуты в обеих моделях читаются через `TryDecodeValue(…ValueBindings.AsString.…, out string)`.

С `--trusted-root` инспектор строит цепочку из сертификатов CMS, сертификатов `--certificate` и явно переданных доверенных корней. Для каждого звена он сопоставляет имена issuer/subject и, если есть расширение `AuthorityKeyIdentifier`, дополнительно сверяет его `keyIdentifier` с `SubjectKeyIdentifier` кандидата, а пару `authorityCertIssuer`/`authorityCertSerialNumber` — с именем издателя и серийным номером кандидата. При отсутствии `AuthorityKeyIdentifier` используется имя issuer/subject. Поддерживается `directoryName` в `authorityCertIssuer`; неподходящие кандидаты и неполная пара отклоняются. Инспектор отклоняет отсутствующее или неоднозначное звено и циклы, проверяет совпадение внутреннего и внешнего алгоритмов подписи сертификата и передаёт исходный TLV TBS, подпись и сертификат издателя криптографическому verifier. Исходные TLV сертификатов служат для определения доверенного корня и обнаружения циклов; SPKI передаётся RSA без повторного кодирования. Можно повторять оба ключа для нескольких сертификатов. При этом режиме `RsaSha256CryptoVerifier<T>` проверяет SHA-256 и подписи RSA PKCS#1 v1.5 для CMS и каждого звена. Другие алгоритмы отклоняются. Без `--trusted-root` остаётся учебный режим с заглушкой `EducationalCryptoVerifier<T>`.

| Операция | `Asn1Kit.Pkix` | `Asn1Kit.Modern` (`CryptographicMessageSyntax2009`) |
| --- | --- | --- |
| Вложенное содержимое | `ReadOnlyMemory<byte>` | `Asn1Contained<T>.Contents`; содержимое `id-data` остаётся непрозрачным |
| Сертификат | `CertificateChoices.Certificate.Value` и `EncodedMemory` из lazy-обёртки | `CertificateChoices.Certificate.Value` и `OriginalEncoding`; `Certificate.ToBeSignedOriginalEncoding` |
| Сопоставление issuer | Сравниваются исходные TLV имён сертификата и подписанта | Сравниваются исходные TLV имён сертификата и подписанта через `OriginalEncoding` |
| Байты `signedAttrs` для подписи | Исходный TLV доступен через `OriginalEncoding`; runtime меняет внешний `[0]` на `SET` | То же; поле имеет тип `Asn1Value<Attribute[]>?` |
| Печать имени | `AttributeTypeAndValue[][]` со строковым choice | `SingleAttribute[][]` со значением `Asn1Any` |

Для modern-сборки `retainEncoded` у `Certificate.toBeSigned`, `TBSCertificate.issuer/subject/subjectPublicKeyInfo`, `CertificateChoices.certificate`, `IssuerAndSerialNumber.issuer` и `SignerInfo.signedAttrs` задаётся в [options patch](../../../compiler/fixtures/ir/modern-pkix-cms.patch.json). Для `Asn1Kit.Pkix` те же verify-options (плюс lazy `CertificateChoices.certificate`) задаёт [dvcs.patch.json](../../../compiler/fixtures/ir/dvcs.patch.json). Демо использует модуль CMS `-2009` в режиме `--modern`.

Один интерфейс `ICmsCryptoVerifier<TCertificate>` и две его реализации обслуживают оба варианта generated API. Код завершения: `0` — проверки выбранного режима пройдены; `1` — контейнер или подписант отклонён; `2` — ошибка аргументов либо чтения файла. Демонстрационный алгоритм цепочки не проверяет срок действия, назначение и отзыв сертификатов, политику CA и ограничения пути; доверие определяется только переданными корнями. Detached-контейнеры не поддерживаются. Сравнение касается удобства API, не производительности.
