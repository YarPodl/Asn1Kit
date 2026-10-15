# Asn1Kit.Cms.Demo

Консольные подкоманды `verify`, `print-cert`, `print-cms` и `sign`. По умолчанию — **Asn1Kit.Modern** (CMS-2009). Флаг `--legacy` включает regression-стек `Asn1Kit.Legacy` (ASN.1:1988).

Запуск из корня репозитория:

```powershell
dotnet run --project runtime-csharp/examples/Asn1Kit.Cms.Demo -- verify path/to/attached.p7m
dotnet run --project runtime-csharp/examples/Asn1Kit.Cms.Demo -- verify --legacy path/to/attached.p7m
dotnet run --project runtime-csharp/examples/Asn1Kit.Cms.Demo -- print-cert path/to/cert.cer
dotnet run --project runtime-csharp/examples/Asn1Kit.Cms.Demo -- print-cms path/to/attached.p7m
dotnet run --project runtime-csharp/examples/Asn1Kit.Cms.Demo -- sign --certificate path/to/cert.cer --content path/to/data.bin --output out.p7m
```

| Операция | Default (`Asn1Kit.Modern`) | `--legacy` (`Asn1Kit.Legacy`) |
| --- | --- | --- |
| Certificate | `PKIX1Explicit2009.Certificate` / `Signed<T>` | `Legacy.Pkix.Certificate` / `Asn1Value` TBS |
| CMS | `CryptographicMessageSyntax2009` | `CryptographicMessageSyntax2004` |

Options: modern — [modern-pkix-cms.patch.json](../../../compiler/fixtures/ir/modern-pkix-cms.patch.json); legacy — [dvcs.patch.json](../../../compiler/fixtures/ir/dvcs.patch.json).

Modern-ветка использует generated open-type bindings для `ContentInfo`, signed attributes и расширений сертификата (`Create`, `TryDecodeContent`, `TryGet`). Legacy-ветка использует тот же API там, где bindings доступны; `Attribute.attrValues` остаётся raw `Asn1Any`, поскольку для Legacy CMS каталог bindings не генерируется.
