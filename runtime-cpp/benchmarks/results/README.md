# Benchmark result snapshots

Google Benchmark console and optional `--benchmark_out=*.json` are not committed from the build tree. After a Release run, copy artefacts here.

```powershell
$dest = "runtime-cpp/benchmarks/results/YYYY-MM-DD"
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item bench.json "$dest/"
# Add SUMMARY.md: git SHA, host/toolchain/dependency versions, exact command,
# fixture sizes, and aggregate Mean for all 13 benchmarks.
```

The JSON context must include the OpenSSL version reported by the benchmark binary. A snapshot describes only its recorded host and is not a performance gate. Update the “current baseline” section in [../README.md](../README.md).
