# Asn1Kit.Pkix

Golden C# for PKIX/CMS and the complete DVCS dependency graph. Namespaces are `Asn1Kit.Pkix`, `.Cms`, `.Pkcs10`, `.Crmf`, `.Cmp`, `.Ocsp`, `.Ess`, `.Smime`, and `.Dvcs`.

Files `*.g.cs` are **auto-generated** — do not edit by hand. Diffs here are reviewed like IR golden fixtures.

## Regenerate

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/regenerate-csharp-golden.ps1
```

Скрипт создаёт промежуточный DVCS IR во временном каталоге и удаляет его после генерации. PKIX/CMS JSON goldens остаются compiler regression fixtures. DVCS проверяется shape assertions и регенерацией исходников, а не закоммиченным JSON IR.
