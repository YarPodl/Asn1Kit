# CMS fixtures

## Independent RFC 4134 corpus

See [rfc4134/](rfc4134/) — published CMS samples, `expected.json` from `SignedCms` + `System.Formats.Asn1`, used by `CmsRfc4134InteropTests` and C++ `generated_modern_cms_tests`.

## `attached-signeddata.p7m`

Minimal attached PKCS#7 / CMS `SignedData` (`ContentInfo`) used by `Asn1Kit.Legacy.Benchmarks` and educational CMS demo tests.

Provenance: generated locally with .NET 6 `System.Security.Cryptography.Pkcs.SignedCms` and a self-signed RSA-2048 certificate (`CN=Asn1Kit CMS Bench`), SHA-256, end-entity cert embedded. Payload UTF-8 text: `Asn1Kit CMS benchmark payload`.

Not a NIST/RFC sample; regenerate only if the format must change (then re-verify smoke in the benchmark project).
