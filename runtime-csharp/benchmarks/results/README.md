# Benchmark result snapshots

Dated folders under this path are **checked-in baselines** for comparing optimizations.
`BenchmarkDotNet.Artifacts/` (repo root) is gitignored — copy reports here after a run.

## Same machine vs full suite

На **той же машине** (тот же OS/.NET/CPU) после оптимизаций Asn1Kit достаточно краткого прогона Decode+Encode KPI; BCL и BouncyCastle от железа не меняются, их цифры бери из полного baseline ([2026-09-23](2026-09-23/) или новее с `--filter *`).

Полный suite (`--filter *`) — при смене машины, SDK/runtime, фикстур или peer-библиотек.

```powershell
# краткий прогон после правок Asn1Kit (Cert/CRL + CMS Lazy, Decode и Encode; PowerShell)
dotnet run -c Release --project runtime-csharp/benchmarks/Asn1Kit.Pkix.Benchmarks -- --filter "*CertificateBenchmarks.Asn1Kit_Decode" "*CertificateBenchmarks.Asn1Kit_Encode" "*CertificateListBenchmarks.Asn1Kit_Decode" "*CertificateListBenchmarks.Asn1Kit_Encode" "*CmsBenchmarks.Asn1Kit_Lazy_Decode" "*CmsBenchmarks.Asn1Kit_Lazy_Encode"

# инвентарь аллокаций (без BDN)
dotnet run -c Release --project runtime-csharp/benchmarks/Asn1Kit.Pkix.Benchmarks -- --alloc-profile

# все Asn1Kit-методы без peers
dotnet run -c Release --project runtime-csharp/benchmarks/Asn1Kit.Pkix.Benchmarks -- --filter *Asn1Kit_*

# полный suite (peers + Asn1Kit)
dotnet run -c Release --project runtime-csharp/benchmarks/Asn1Kit.Pkix.Benchmarks -- --filter *
```

## How to add a run

```powershell
# см. фильтр выше
$dest = "runtime-csharp/benchmarks/results/YYYY-MM-DD"
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item BenchmarkDotNet.Artifacts/results/Asn1Kit.Benchmarks.*-report-github.md $dest/
Copy-Item BenchmarkDotNet.Artifacts/results/Asn1Kit.Benchmarks.*-report.csv $dest/
# optional: full log
Copy-Item (Get-ChildItem BenchmarkDotNet.Artifacts/BenchmarkRun-*.log | Sort-Object LastWriteTime -Descending | Select-Object -First 1) "$dest/BenchmarkRun.log"
```

В `SUMMARY.md`: git SHA, фильтр (краткий / `*Asn1Kit_*` / `*`), и таблицы Asn1Kit; для peers укажи ссылку на полный baseline, если прогон был без peers.
Update the “current baseline” section in [../Asn1Kit.Pkix.Benchmarks/README.md](../Asn1Kit.Pkix.Benchmarks/README.md).

## Index

| Folder | Git | Notes |
| --- | --- | --- |
| [2026-09-23](2026-09-23/) | `7d281d1` | Полный suite (peers + Asn1Kit); эталон BCL/BC для Asn1Kit-прогонов на этой машине |
| [2026-09-23-of-arrays](2026-09-23-of-arrays/) | working tree | `ReadSequenceOf` → `T[]` / ArrayPool; backlog §8; Asn1Kit Alloc↓; микробенч Fill A/B/C (архив) |
| [2026-09-24-sequence-lambda](2026-09-24-sequence-lambda/) | working tree | `EnterSequence`; OF без capturing-лямбды; микробенч Nest/Of; Decode Alloc↓ ~30% |
| [2026-09-24-explicit-enter](2026-09-24-explicit-enter/) | working tree | `EnterExplicit`; удалены `ReadSequence(Func)`/`ReadSet`; Alloc ≈ sequence-lambda |
| [2026-09-30-writer-optimizations](2026-09-30-writer-optimizations/) | `eb0984b` + working tree | reserve-one constructed length; lookup однобайтовых INTEGER; micro + encode before/after |
| [2026-10-09-decode-kpi](2026-10-09-decode-kpi/) | `5180296` | Decode KPI после typed open-type; Alloc↑ vs explicit-enter; причина — `AttributeTypeAndValue_Value` + `AlgorithmIdentifier_Parameters` |
| [2026-10-10-decode-encode-kpi](2026-10-10-decode-encode-kpi/) | `8dc394b` + bench fix | Decode+Encode после Binding unify; Cert/CRL Alloc↓; CMS Lazy = ContentInfo+SignedData (1.08 KB) |
