# Asn1Kit.Pkix.Benchmarks

Сравнение Decode/Encode **Certificate**, **CertificateList** (CRL) и CMS **ContentInfo** (attached SignedData):

| Library | Certificate | CRL | CMS |
| --- | --- | --- | --- |
| Asn1Kit | Decode + Encode | Decode + Encode | Decode + Encode |
| BCL (`X509Certificate2` / `SignedCms`) | Decode; Encode = `RawData` copy | — (нет typed API на net6.0) | Decode + Encode |
| BouncyCastle | Decode + `GetEncoded` | Decode + `GetEncoded` | Decode + `GetEncoded` |

Фикстуры: [fixtures/pkix](../../fixtures/pkix/), [fixtures/cms](../../fixtures/cms/).

Не входит в `dotnet test`. Запуск:

```powershell
dotnet run -c Release --project runtime-csharp/benchmarks/Asn1Kit.Pkix.Benchmarks
```

Точечно (после smoke):

```powershell
dotnet run -c Release --project runtime-csharp/benchmarks/Asn1Kit.Pkix.Benchmarks -- --filter *Certificate*
```
