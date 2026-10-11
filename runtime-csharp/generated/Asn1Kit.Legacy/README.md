# Asn1Kit.Legacy

Regression-only golden C# for the ASN.1:1988 PKIX/CMS/DVCS dependency graph. Not the product surface — prefer [`Asn1Kit.Modern`](../Asn1Kit.Modern/).

Namespaces: `Asn1Kit.Legacy.Pkix`, `.Cms`, `.Pkcs10`, `.Crmf`, `.Cmp`, `.Ocsp`, `.Ess`, `.Smime`, `.Dvcs`.

Product decode options (from [`dvcs.patch.json`](../../../compiler/fixtures/ir/dvcs.patch.json)):

- `lazy` on `CertificateChoices.certificate`
- `retainEncoded` on `Certificate.tbsCertificate`, TBS Name/SPKI/Extensions, CMS issuer/`signedAttrs`
- `AttributeTypeAndValue` as a C# `struct`

Files `*.g.cs` are **auto-generated** — do not edit by hand.

## Regenerate

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/regenerate-csharp-golden.ps1
```

The script builds a temporary DVCS IR and deletes it after generate. PKIX/CMS JSON goldens remain compiler regression fixtures (without product options).
