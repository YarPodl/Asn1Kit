# Asn1Kit.Pkix

Golden C# for PKIX1Explicit88 + PKIX1Implicit88 (`Asn1Kit.Pkix`) and CryptographicMessageSyntax2004 (`Asn1Kit.Cms`).

Files `*.g.cs` are **auto-generated** — do not edit by hand. Diffs here are reviewed like IR golden fixtures.

## Regenerate

```powershell
# IR (Explicit + Implicit + CMS); then set csharp.namespace in JSON:
#   PKIX1Explicit88 / PKIX1Implicit88 → Asn1Kit.Pkix
#   CryptographicMessageSyntax2004 → Asn1Kit.Cms
dotnet run --project compiler/src/Asn1Kit.Cli -- compile `
  -i compiler/fixtures/asn1/pkix1-explicit88.asn `
  -i compiler/fixtures/asn1/pkix1-implicit88.asn `
  -i compiler/fixtures/asn1/cms-2004.asn `
  -o compiler/fixtures/ir/cms-2004.json `
  --bindings compiler/fixtures/opentype/pkix-bindings.json `
  --bindings compiler/fixtures/opentype/cms-bindings.json

dotnet run --project compiler/src/Asn1Kit.Cli -- generate `
  -i compiler/fixtures/ir/cms-2004.json --lang csharp `
  -o runtime-csharp/generated/Asn1Kit.Pkix
```

Source IR: `compiler/fixtures/ir/cms-2004.json`. PKIX-only goldens `pkix1-explicit88.json` / `pkix1-implicit88.json` remain for compiler regression tests.
