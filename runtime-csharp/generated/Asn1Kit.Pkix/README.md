# Asn1Kit.Pkix

Golden C# output of the code generator for PKIX1Explicit88 + PKIX1Implicit88.

Files `*.g.cs` are **auto-generated** — do not edit by hand. Diffs here are reviewed like IR golden fixtures.

## Regenerate

```powershell
dotnet run --project compiler/src/Asn1Kit.Cli -- generate `
  -i compiler/fixtures/ir/pkix1-implicit88.json --lang csharp `
  --csharp-namespace Asn1Kit.Pkix `
  -o runtime-csharp/generated/Asn1Kit.Pkix
```

Source IR: `compiler/fixtures/ir/pkix1-implicit88.json` (from both `.asn` modules). Rebuild that IR first if the ASN.1 or compiler changed.
