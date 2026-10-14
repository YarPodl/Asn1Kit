# Benchmark result snapshots

Google Benchmark console and optional `--benchmark_out=*.json` are not committed from the build tree. After a Release run, copy artefacts here.

```powershell
$dest = "runtime-cpp/benchmarks/results/YYYY-MM-DD"
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item bench.json "$dest/"   # if --benchmark_out was used
# Paste console table into SUMMARY.md (git SHA, host, filter, Mean)
```

Update the “current baseline” section in [../README.md](../README.md).
