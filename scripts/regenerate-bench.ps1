[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot

Push-Location $repositoryRoot
try {
    & dotnet run --project compiler/src/Asn1Kit.Cli -- generate `
        -i compiler/fixtures/ir/cms-2004.json `
        --patch compiler/fixtures/ir/cms-2004-bench.patch.json `
        --ir-output compiler/fixtures/ir/cms-2004-bench.json `
        --lang csharp `
        -o runtime-csharp/generated/Asn1Kit.Pkix.Bench

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}
