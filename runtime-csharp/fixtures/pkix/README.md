# PKIX encode/decode fixtures (NIST PKITS)

## Provenance

DER samples are a **slice** of the NIST [Public Key Interoperability Test Suite (PKITS)](https://csrc.nist.gov/projects/pki-testing), public domain (United States Government Work under 17 U.S.C. 105). Files were taken from the BoringSSL mirror of `PKITS_data.zip` (`certs/` / `crls/`):

- https://github.com/google/boringssl/tree/master/pki/testdata/nist-pkits

| File | Role |
| --- | --- |
| `TrustAnchorRootCertificate.crt` | Trust Anchor Root Certificate (self-signed) |
| `GoodCACert.crt` | Good CA Cert |
| `GoodCACRL.crl` | Good CA CRL |

DN naming is also documented in NIST PKITS materials and in `pkits.ldif` (e.g. `CN=Good CA`, `CN=Trust Anchor`, `O=Test Certificates 2011`, `C=US`).

## Expected values (`expected.json`)

Spot-check numbers and strings come from **independent dumps of these same bytes**, not from Asn1Kit decode:

1. **Certificates** — `System.Security.Cryptography.X509Certificates.X509Certificate2` (see `certs.bcl-dump.txt`) and Windows `certutil -dump` (`*.certutil.txt`).
2. **CRL** — Windows `certutil -dump` (`GoodCACRL.certutil.txt`). `ThisUpdate` / `NextUpdate` / revoked serials / CRL extension OIDs are taken from that dump. Local times in certutil (e.g. `11:30` on UTC+3) are converted to UTC `08:30`, matching BCL `NotBefore`/`NotAfter` on the related certificates.

OpenSSL was not available on the authoring machine; certutil + BCL are the committed oracles.
