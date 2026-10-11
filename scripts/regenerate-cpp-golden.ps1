# Regenerates committed C++ codegen golden under runtime-cpp/generated/.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$out = "runtime-cpp/generated/example"
New-Item -ItemType Directory -Force -Path $out | Out-Null
Get-ChildItem -LiteralPath $out -Filter '*.hpp' -ErrorAction SilentlyContinue | Remove-Item -Force
dotnet run --project compiler/src/Asn1Kit.Cli -- generate `
  -i compiler/fixtures/ir/example.json `
  --lang cpp `
  -o $out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$openOut = "runtime-cpp/generated/open-binding"
New-Item -ItemType Directory -Force -Path $openOut | Out-Null
Get-ChildItem -LiteralPath $openOut -Filter '*.hpp' -ErrorAction SilentlyContinue | Remove-Item -Force
dotnet run --project compiler/src/Asn1Kit.Cli -- generate `
  -i compiler/fixtures/ir/cpp-open-binding.json `
  --lang cpp `
  -o $openOut
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$extOut = "runtime-cpp/generated/extensible"
New-Item -ItemType Directory -Force -Path $extOut | Out-Null
Get-ChildItem -LiteralPath $extOut -Filter '*.hpp' -ErrorAction SilentlyContinue | Remove-Item -Force
dotnet run --project compiler/src/Asn1Kit.Cli -- generate `
  -i compiler/fixtures/ir/cpp-extensible.json `
  --lang cpp `
  -o $extOut
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$defOut = "runtime-cpp/generated/deferred"
New-Item -ItemType Directory -Force -Path $defOut | Out-Null
Get-ChildItem -LiteralPath $defOut -Filter '*.hpp' -ErrorAction SilentlyContinue | Remove-Item -Force
dotnet run --project compiler/src/Asn1Kit.Cli -- generate `
  -i compiler/fixtures/ir/cpp-deferred.json `
  --lang cpp `
  -o $defOut
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Updated $out, $openOut, $extOut, $defOut"
