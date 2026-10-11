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

$openIr = "compiler/fixtures/ir/cpp-open-binding.json"
$openOut = "compiler/fixtures/codegen-cpp/open-binding"
New-Item -ItemType Directory -Force -Path $openOut | Out-Null
dotnet run --project compiler/src/Asn1Kit.Cli -- generate `
  -i $openIr `
  --lang cpp `
  -o $openOut
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$openRuntime = "runtime-cpp/generated/open-binding"
New-Item -ItemType Directory -Force -Path $openRuntime | Out-Null
Copy-Item -Force "$openOut/M.hpp" "$openRuntime/M.hpp"

Write-Host "Updated $out, $runtimeOut, $openOut, $openRuntime"
