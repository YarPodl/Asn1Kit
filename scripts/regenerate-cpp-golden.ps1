# Regenerates committed C++ codegen golden for example.json.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$out = "compiler/fixtures/codegen-cpp/example"
New-Item -ItemType Directory -Force -Path $out | Out-Null
dotnet run --project compiler/src/Asn1Kit.Cli -- generate `
  -i compiler/fixtures/ir/example.json `
  --lang cpp `
  -o $out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$runtimeOut = "runtime-cpp/generated/example"
New-Item -ItemType Directory -Force -Path $runtimeOut | Out-Null
Copy-Item -Force "$out/ExampleModule.hpp" "$runtimeOut/ExampleModule.hpp"
Write-Host "Updated $out and $runtimeOut"
