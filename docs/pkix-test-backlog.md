# Беклог тестов Asn1Kit.Legacy (encode/decode)

Область: **ASN.1 encode/decode** сгенерированных PKIX/CMS структур в `Asn1Kit.Legacy.Tests`.  
Не входит: path validation, signature verify, CMS decrypt/encrypt crypto — слой «инструменты PKI» ([status.md](status.md)).

## Принцип фикстур

См. [runtime-csharp/fixtures/pkix/README.md](../runtime-csharp/fixtures/pkix/README.md):

- байты — NIST PKITS (зеркало BoringSSL) или иные **внешние** DER / hex;
- `expected.json` — из независимых дампов тех же байтов (`certutil`, `X509Certificate2`, `System.Formats.Asn1`);
- round-trip: `Decode` → `Encode` → byte-identical к исходному DER;
- **запрещено** брать expected fields из `Asn1Kit.*.Decode` или эталонные байты из `Asn1Kit.*.Encode` при отладке.
- schema constraints (`SIZE`, диапазоны, `WITH COMPONENTS`, presence, закрытость object sets) не являются reject-критериями: тесты проверяют поля и безопасный decode, а не прикладную валидацию;
- для поддержанной неканонической BER-формы ожидается успешный decode и канонический последующий DER encode; byte-identical round-trip обязателен только для исходного канонического DER.

Статусы: `todo` | `done` | `deferred` (нет внешнего вектора / нет модуля).

---

## Сделано (P0 срез)

`Certificate` / `CertificateList` / `GeneralName` / negative: TrustAnchor, GoodCA, EE, NameConstraints CA (SPKI, signature, DN, Validity, typed extensions, BCL, DER round-trip); GoodCACRL (entries + CRLReason, CRLNumber, AKI); SAN dNSName/rfc822/URI (PKITS) + iPAddress/registeredID (Formats.Asn1); truncated / wrong-tag Certificate & CRL.

Typed extensions: KeyUsage, BasicConstraints, AKI, SKI, CertificatePolicies, CRLNumber, SAN, NameConstraints; round-trip typed `extnValue`. GeneralName `otherName` (XMPP, DNS SRV, SMTPUTF8). Printable/UTF8 DirectoryString из PKITS DN.

---

## P0 — открыто / deferred

### Certificate / TBSCertificate

| Кейс | Статус |
| --- | --- |
| Encode-from-scratch без внешнего эталона | deferred |
| UTCTime vs GeneralizedTime в Validity | deferred |
| v1 cert без extensions / empty subject + SAN critical | deferred |
| issuerUniqueID / subjectUniqueID | deferred |

### Extensions (typed decode `extnValue`)

| Кейс | Статус |
| --- | --- |
| `ExtKeyUsage` SEQUENCE OF OID | deferred |
| `CRLDistributionPoints` | deferred |
| `AuthorityInfoAccess` / `SubjectInfoAccess` | deferred |
| `PolicyConstraints` / `PolicyMappings` / `InhibitAnyPolicy` | deferred |
| `PrivateKeyUsagePeriod` | deferred |

### CRL / CertificateList

| Кейс | Статус |
| --- | --- |
| Entry `invalidityDate` / `certificateIssuer` | deferred |
| Empty `revokedCertificates` | deferred |
| `IssuingDistributionPoint` | deferred |
| Delta CRL / FreshestCRL | deferred |
| CRL без `nextUpdate` | deferred |
| Mismatched outer vs TBS signature AlgorithmIdentifier | deferred |

### Name / GeneralName / DirectoryString

| Кейс | Статус |
| --- | --- |
| Остальные DirectoryString kinds round-trip | deferred |
| `GeneralName.directoryName` | deferred |
| Multi-valued RDN | deferred |
| Escaped DN chars | deferred |
| Минимальный `ORAddress` | deferred |

### Negative / malformed

| Кейс | Статус |
| --- | --- |
| Non-minimal length / duplicate OID / indefinite in DER | deferred |

---

## P1 — модули позже

### CSR / PKCS#10

| Кейс | Статус |
| --- | --- |
| Decode CSR: version, subject, SPKI, attributes | todo |
| Attribute `extensionRequest` | todo |
| Empty attributes / challengePassword | todo |
| DER round-trip; reject truncated | todo |

### OCSP (RFC 6960)

| Кейс | Статус |
| --- | --- |
| `OCSPRequest` / `TBSRequest` | todo |
| `CertID` | todo |
| `OCSPResponse` + `ResponseBytes` | todo |
| `BasicOCSPResponse` / `SingleResponse` (good/revoked/unknown) | todo |
| Extensions: nonce | todo |
| DER round-trip внешних dumps | todo |

---

## CMS / PKCS#7 (ASN.1/IR/C# **добавлены**; codec-кейсы — вторая половина; базовые DVCS codec-кейсы есть)

Ориентиры: BouncyCastle `cms/test`, OpenSSL `80-test_cms.t`, .NET `SignedCms` / `EnvelopedCms`.  
Типы: `Asn1Kit.Cms` из [cms-2004.asn](../compiler/fixtures/asn1/cms-2004.asn) / golden [cms-2004.json](../compiler/fixtures/ir/cms-2004.json).

### ContentInfo + общие

| Кейс | Статус |
| --- | --- |
| `ContentInfo` decode по contentType OID | done (RFC 4134 `3.2` / SignedData outer) |
| BER indefinite + DER definite контейнера | todo |
| Trailing junk after ContentInfo — зафиксировать политику | todo |

### SignedData

| Кейс | Статус |
| --- | --- |
| Attached SignedData (version, digests, eContent, certs, crls, signerInfos) | done (RFC 4134 `4.2`) |
| Detached SignedData | done (RFC 4134 `4.3`) |
| Multiple `SignerInfo` (IssuerAndSerialNumber + SKID) | done (`4.6` multi; `4.7` SKI) |
| `signedAttrs` / `unsignedAttrs` (contentType, messageDigest, signingTime, countersignature) | partial (`4.10` contentType/messageDigest + retained SET; countersignature / unsorted — open) |
| Embedded certs + CRLs | done (certs in `4.2`/`4.6`; CRL vectors — open) |
| RFC 4134 sample vectors (field asserts, без обязательного verify) | done (`3.2`, `4.2`, `4.3`, `4.6`, `4.7`, `4.10`) |
| SET OF DER sort (digestAlgorithms / certificates / signerInfos) | todo |

### Прочие CMS content types

| Кейс | Статус |
| --- | --- |
| `EnvelopedData` KeyTransRecipientInfo | todo |
| KeyAgreeRecipientInfo (структура) | todo |
| KEKRecipientInfo / PasswordRecipientInfo | todo |
| Nested SignedData ↔ EnvelopedData | todo |
| `AuthEnvelopedData` (RFC 5083) | todo |
| `EncryptedData` / `DigestedData` / `CompressedData` / `AuthenticatedData` | todo |
| UnprotectedAttrs round-trip | todo |

### CMS interop-векторы

| Источник | Статус |
| --- | --- |
| RFC 4134 §4.x sample `.bin` | done ([rfc4134/](../runtime-csharp/fixtures/cms/rfc4134/)) |
| OpenSSL `test/smime-certs` CMS DER | todo |
| .NET Pkcs fixtures | todo |
| BC `CMSSampleMessages` | todo |

---

## P2 — interop / расширение

| Кейс | Статус |
| --- | --- |
| Больший срез NIST PKITS (decode + round-trip, не path-build) | todo |
| Второй oracle BouncyCastle при расхождении с BCL | todo |
| OpenSSL dump как expected | todo |
| AlgorithmIdentifier params NULL vs absent (RSA) | todo |
| ECDSA / Ed25519 SPKI + sig alg params | todo |
| Attribute Certificate (RFC 5755) | todo |

---

## Рекомендуемый порядок

1. ~~Каркас CMS-тестов на модуле `Asn1Kit.Cms` (RFC 4134).~~ done — `CmsRfc4134InteropTests`.
2. CSR/OCSP — когда появятся ASN.1 модули.
3. Не тащить path validation / verify / decrypt в `Asn1Kit.Legacy.Tests`.
