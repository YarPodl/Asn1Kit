# Asn1Kit.Pkix.Bench

Generated C# for PKIX + CMS with **bench** options overlay (not the golden product surface).

- Namespaces: `Asn1Kit.Pkix.Bench` / `Asn1Kit.Cms.Bench`
- Source of truth for options: [`compiler/fixtures/ir/cms-2004-bench.patch.json`](../../../compiler/fixtures/ir/cms-2004-bench.patch.json)
- Derived IR: [`compiler/fixtures/ir/cms-2004-bench.json`](../../../compiler/fixtures/ir/cms-2004-bench.json)

Files `*.g.cs` are **auto-generated** — do not edit by hand.

## Regenerate

Requires up-to-date golden [`cms-2004.json`](../../../compiler/fixtures/ir/cms-2004.json) (see [Asn1Kit.Pkix README](../Asn1Kit.Pkix/README.md)).

```powershell
dotnet run --project compiler/src/Asn1Kit.Cli -- generate `
  -i compiler/fixtures/ir/cms-2004.json `
  --patch compiler/fixtures/ir/cms-2004-bench.patch.json `
  --ir-output compiler/fixtures/ir/cms-2004-bench.json `
  --lang csharp `
  -o runtime-csharp/generated/Asn1Kit.Pkix.Bench
```

Used by [Asn1Kit.Pkix.Benchmarks](../../benchmarks/Asn1Kit.Pkix.Benchmarks/). Golden [`Asn1Kit.Pkix`](../Asn1Kit.Pkix/) remains for tests.
