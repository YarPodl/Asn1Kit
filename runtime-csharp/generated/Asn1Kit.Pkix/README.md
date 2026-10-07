# Asn1Kit.Pkix

Golden C# for PKIX/CMS and the complete DVCS dependency graph. Namespaces are `Asn1Kit.Pkix`, `.Cms`, `.Pkcs10`, `.Crmf`, `.Cmp`, `.Ocsp`, `.Ess`, `.Smime`, and `.Dvcs`.

Product decode options (from [`dvcs.patch.json`](../../../compiler/fixtures/ir/dvcs.patch.json)):

- `lazy` on `CertificateChoices.certificate`
- `retainEncoded` on `Certificate.tbsCertificate`, TBS Name/SPKI/Extensions, CMS issuer/`signedAttrs`
- `AttributeTypeAndValue` as a C# `struct`

Files `*.g.cs` are **auto-generated** — do not edit by hand. Diffs here are reviewed like IR golden fixtures.

## Regenerate

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/regenerate-csharp-golden.ps1
```

Скрипт создаёт промежуточный DVCS IR во временном каталоге и удаляет его после генерации. PKIX/CMS JSON goldens остаются compiler regression fixtures (без product options). DVCS проверяется shape assertions и регенерацией исходников, а не закоммиченным JSON IR.
