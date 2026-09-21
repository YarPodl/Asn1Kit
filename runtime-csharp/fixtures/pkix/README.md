# PKIX encode/decode fixtures (NIST PKITS)

## Provenance

DER samples are a **slice** of the NIST [Public Key Interoperability Test Suite (PKITS)](https://csrc.nist.gov/projects/pki-testing), public domain (United States Government Work under 17 U.S.C. 105). Files were taken from the BoringSSL mirror of `PKITS_data.zip` (`certs/` / `crls/`):

- https://github.com/google/boringssl/tree/master/pki/testdata/nist-pkits

| File | Role |
| --- | --- |
| `TrustAnchorRootCertificate.crt` | Trust Anchor Root Certificate (self-signed) |
| `GoodCACert.crt` | Good CA Cert |
| `GoodCACRL.crl` | Good CA CRL (entries + CRLReason keyCompromise) |
| `ValidCertificatePathTest1EE.crt` | EE certificate under Good CA |
| `nameConstraintsDNS1CACert.crt` | CA with NameConstraints (permitted DNS) |
| `ValidDNSnameConstraintsTest30EE.crt` | EE with SAN dNSName |
| `ValidRFC822nameConstraintsTest21EE.crt` | EE with SAN rfc822Name |
| `ValidURInameConstraintsTest34EE.crt` | EE with SAN uniformResourceIdentifier |

DN naming is also documented in NIST PKITS materials and in `pkits.ldif`.

## Expected values (`expected.json`)

Spot-check numbers and strings come from **independent dumps of these same bytes**, not from Asn1Kit decode:

1. **Certificates** — `System.Security.Cryptography.X509Certificates.X509Certificate2` (see `certs.bcl-dump.txt`) and Windows `certutil -dump` (`*.certutil.txt`).
2. **CRL** — Windows `certutil -dump` (`GoodCACRL.certutil.txt`). Local times in certutil (e.g. `11:30` on UTC+3) are converted to UTC. Per-entry revocation seconds come from the UTCTime strings in the DER (certutil omits seconds; entry serial `0f` is `100101083001Z` = `08:30:01Z`).
3. **SPKI / signature lengths** — BCL `PublicKey.EncodedKeyValue` and `System.Formats.Asn1` BIT STRING parse of the outer signature.
4. **Synthetic `generalNames` hex** — produced with `System.Formats.Asn1.AsnWriter` (context-specific tags per RFC 5280), not Asn1Kit.

OpenSSL was not required for this slice; certutil + BCL + `System.Formats.Asn1` are the committed oracles.

**Do not** regenerate `expected.json` fields from `Asn1Kit.*.Decode` or use `Asn1Kit.*.Encode` as the source of truth when debugging.
