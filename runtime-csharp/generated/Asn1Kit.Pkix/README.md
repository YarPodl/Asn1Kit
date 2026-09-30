# Asn1Kit.Pkix

Golden C# for PKIX/CMS and the complete DVCS dependency graph. Namespaces are `Asn1Kit.Pkix`, `.Cms`, `.Pkcs10`, `.Crmf`, `.Cmp`, `.Ocsp`, `.Ess`, `.Smime`, and `.Dvcs`.

Files `*.g.cs` are **auto-generated** — do not edit by hand. Diffs here are reviewed like IR golden fixtures.

## Regenerate

```powershell
# The intermediate DVCS IR is intentionally temporary and is not a golden fixture.
dotnet run --project compiler/src/Asn1Kit.Cli -- compile `
  -i compiler/fixtures/asn1/pkix1-explicit88.asn `
  -i compiler/fixtures/asn1/pkix1-implicit88.asn `
  -i compiler/fixtures/asn1/cms-2004.asn `
  -i compiler/fixtures/asn1/pkcs10.asn `
  -i compiler/fixtures/asn1/pkixcrmf.asn `
  -i compiler/fixtures/asn1/pkixcmp.asn `
  -i compiler/fixtures/asn1/ocsp.asn `
  -i compiler/fixtures/asn1/ess.asn `
  -i compiler/fixtures/asn1/smime-v3.asn `
  -i compiler/fixtures/asn1/dvcs.asn `
  -o $env:TEMP/asn1kit-dvcs.json `
  --bindings compiler/fixtures/opentype/pkix-bindings.json `
  --bindings compiler/fixtures/opentype/cms-bindings.json

dotnet run --project compiler/src/Asn1Kit.Cli -- generate `
  -i $env:TEMP/asn1kit-dvcs.json `
  --patch compiler/fixtures/ir/dvcs.patch.json `
  --lang csharp `
  -o runtime-csharp/generated/Asn1Kit.Pkix
```

PKIX/CMS JSON goldens remain compiler regression fixtures. DVCS is checked by shape assertions and generated-source regeneration rather than a committed JSON IR.
