# RFC 4134 CMS interop fixtures

Independent Cryptographic Message Syntax samples from [RFC 4134](https://www.rfc-editor.org/rfc/rfc4134) Appendix A.

## Provenance

| File | RFC section | Role |
| --- | --- | --- |
| `3.2.bin` | §3.2 | `ContentInfo` with `id-data`, DER |
| `4.2.bin` | §4.2 | Attached `SignedData`, RSA |
| `4.3.bin` | §4.3 | Detached `SignedData` (no `eContent`) |
| `4.6.bin` | §4.6 | Multiple signers |
| `4.7.bin` | §4.7 | Signer identified by subjectKeyIdentifier |
| `4.10.bin` | §4.10 | `SignedData` with `signedAttrs` |
| `ExContent.bin` / `detached-content.bin` | §2.1 | External content for §4.3 (`This is some sample content.`) |
| `rfc4134.txt` | — | Full RFC text (source for extraction) |

Extract or refresh binaries (requires `rfc4134.txt`):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/extract-rfc4134-cms.ps1
```

## Expected values (`expected.json`)

Field spot-checks come from **independent dumps of these same bytes**, not from Asn1Kit:

1. Outer structure — `System.Formats.Asn1.AsnReader` (DER).
2. CMS surface — `System.Security.Cryptography.Pkcs.SignedCms` (certificate count, signer digest OIDs, signature lengths).
3. Committed text dumps — `*.signedcms.txt`.

Regenerate expected + dumps (never feed Asn1Kit into this tool):

```powershell
dotnet run --project scripts/dump-rfc4134-cms-expected/DumpRfc4134CmsExpected.csproj -c Release
```

**Do not** regenerate `expected.json` fields from `Asn1Kit.*.Decode` or use `Asn1Kit.*.Encode` as the source of truth when debugging.
