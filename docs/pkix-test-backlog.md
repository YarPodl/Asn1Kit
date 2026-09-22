# Беклог тестов Asn1Kit.Pkix (encode/decode)

Область: **ASN.1 encode/decode** сгенерированных PKIX/CMS структур в `Asn1Kit.Pkix.Tests`.  
Не входит: path validation, signature verify, CMS decrypt/encrypt crypto — слой «инструменты PKI» ([status.md](status.md)).

## Принцип фикстур

См. [runtime-csharp/fixtures/pkix/README.md](../runtime-csharp/fixtures/pkix/README.md):

- байты — NIST PKITS (зеркало BoringSSL) или иные **внешние** DER / hex;
- `expected.json` — из независимых дампов тех же байтов (`certutil`, `X509Certificate2`, `System.Formats.Asn1`);
- round-trip: `Decode` → `Encode` → byte-identical к исходному DER;
- **запрещено** брать expected fields из `Asn1Kit.*.Decode` или эталонные байты из `Asn1Kit.*.Encode` при отладке.

Статусы: `todo` | `done` | `deferred` (нет внешнего вектора / нет модуля).

---

## Что есть сейчас

| Объект | Кейсы | Статус |
| --- | --- | --- |
| `Certificate` | TrustAnchor, GoodCA, EE, NameConstraints CA: SPKI, signature, DN, Validity, typed extensions, BCL, DER round-trip | done |
| `CertificateList` | GoodCACRL: entries + CRLReason, CRLNumber, AKI, DER round-trip | done |
| `GeneralName` | SAN dNSName/rfc822/URI (PKITS) + iPAddress/registeredID (Formats.Asn1 hex) | done |
| Negative | truncated / wrong-tag Certificate & CRL | done |

---

## P0 — уже сгенерированные типы (Implicit88 / Explicit88)

### Certificate / TBSCertificate

| Кейс | Статус |
| --- | --- |
| Decode + assert `SubjectPublicKeyInfo` (alg OID + BIT STRING unused bits / length) | done |
| Assert `signature` BIT STRING (unused bits + contents length) | done |
| `critical` + typed/raw `extnValue` per extension | done |
| EE-сертификат из PKITS (`ValidCertificatePathTest1EE`) | done |
| Encode-from-scratch без внешнего эталона | deferred |
| UTCTime vs GeneralizedTime в Validity | deferred |
| v1 cert без extensions / empty subject + SAN critical | deferred |
| issuerUniqueID / subjectUniqueID | deferred |

### Extensions (typed decode `extnValue`)

| Кейс | Статус |
| --- | --- |
| `KeyUsage` → flags | done |
| `BasicConstraints` cA / pathLen | done |
| `AuthorityKeyIdentifier` keyIdentifier | done |
| `SubjectKeyIdentifier` OCTET STRING | done |
| `CertificatePolicies` → `PolicyInformation` | done |
| `CRLNumber` INTEGER на CRL | done |
| `SubjectAltName` → `GeneralName` (dNSName, rfc822, URI) | done |
| `ExtKeyUsage` SEQUENCE OF OID | deferred |
| `CRLDistributionPoints` | deferred |
| `AuthorityInfoAccess` / `SubjectInfoAccess` | deferred |
| `NameConstraints` (permitted DNS) | done |
| `PolicyConstraints` / `PolicyMappings` / `InhibitAnyPolicy` | deferred |
| `PrivateKeyUsagePeriod` | deferred |
| Round-trip typed extension: Decode(extnValue) → Encode → byte-identical | done |

### CRL / CertificateList

| Кейс | Статус |
| --- | --- |
| Per-entry `revocationDate` + `crlEntryExtensions` | done |
| Entry `CRLReason` enum | done |
| Entry `invalidityDate` / `certificateIssuer` | deferred |
| Empty `revokedCertificates` | deferred |
| `IssuingDistributionPoint` | deferred |
| Delta CRL / FreshestCRL | deferred |
| CRL без `nextUpdate` | deferred |
| Mismatched outer vs TBS signature AlgorithmIdentifier | deferred |

### Name / GeneralName / DirectoryString

| Кейс | Статус |
| --- | --- |
| Printable/UTF8 DirectoryString из PKITS DN | done |
| Остальные DirectoryString kinds round-trip | deferred |
| `GeneralName`: dNSName / rfc822 / URI | done |
| `GeneralName`: iPAddress / registeredID (synthetic Formats.Asn1 hex) | done |
| `GeneralName.directoryName` | deferred |
| `GeneralName.otherName` → `AnotherName` | deferred |
| Multi-valued RDN | deferred |
| Escaped DN chars | deferred |
| Минимальный `ORAddress` | deferred |

### Negative / malformed (P0 срез)

| Кейс | Статус |
| --- | --- |
| Truncated Certificate / CRL | done |
| Wrong tag / unexpected constructed | done |
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

## CMS / PKCS#7 (модуль status.md #17 — ASN.1/IR/C# **добавлены**; codec-кейсы ниже — вторая половина)

Ориентиры: BouncyCastle `cms/test`, OpenSSL `80-test_cms.t`, .NET `SignedCms` / `EnvelopedCms`.  
Типы: `Asn1Kit.Cms` из [cms-2004.asn](../compiler/fixtures/asn1/cms-2004.asn) / golden [cms-2004.json](../compiler/fixtures/ir/cms-2004.json).

### ContentInfo + общие

| Кейс | Статус |
| --- | --- |
| `ContentInfo` decode по contentType OID | todo |
| BER indefinite + DER definite контейнера | todo |
| Trailing junk after ContentInfo — зафиксировать политику | todo |

### SignedData

| Кейс | Статус |
| --- | --- |
| Attached SignedData (version, digests, eContent, certs, crls, signerInfos) | todo |
| Detached SignedData | todo |
| Multiple `SignerInfo` (IssuerAndSerialNumber + SKID) | todo |
| `signedAttrs` / `unsignedAttrs` (contentType, messageDigest, signingTime, countersignature) | todo |
| Embedded certs + CRLs | todo |
| RFC 4134 sample vectors (field asserts, без обязательного verify) | todo |
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
| RFC 4134 §4.x sample `.bin` | todo |
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

1. ~~P0 typed extensions + SPKI/CRL entries + GeneralName на внешних `.crt`/`.crl`.~~
2. Каркас CMS-тестов на модуле `Asn1Kit.Cms` (RFC 4134).
3. CSR/OCSP — когда появятся ASN.1 модули.
4. Не тащить path validation / verify / decrypt в `Asn1Kit.Pkix.Tests`.
