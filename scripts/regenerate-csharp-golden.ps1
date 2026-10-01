[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$temporaryIr = Join-Path ([System.IO.Path]::GetTempPath()) ("asn1kit-dvcs-{0}.json" -f [guid]::NewGuid().ToString('N'))

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]] $Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet failed with exit code $LASTEXITCODE."
    }
}
Push-Location $repositoryRoot
try {
    Invoke-DotNet @(
        'run', '--project', 'compiler/src/Asn1Kit.Cli', '--', 'compile',
        '-i', 'compiler/fixtures/asn1/pkix1-explicit88.asn',
        '-i', 'compiler/fixtures/asn1/pkix1-implicit88.asn',
        '-i', 'compiler/fixtures/asn1/cms-2004.asn',
        '-i', 'compiler/fixtures/asn1/pkcs10.asn',
        '-i', 'compiler/fixtures/asn1/pkixcrmf.asn',
        '-i', 'compiler/fixtures/asn1/pkixcmp.asn',
        '-i', 'compiler/fixtures/asn1/ocsp.asn',
        '-i', 'compiler/fixtures/asn1/ess.asn',
        '-i', 'compiler/fixtures/asn1/smime-v3.asn',
        '-i', 'compiler/fixtures/asn1/dvcs.asn',
        '-o', $temporaryIr,
        '--bindings', 'compiler/fixtures/opentype/pkix-bindings.json',
        '--bindings', 'compiler/fixtures/opentype/cms-bindings.json'
    )

    Invoke-DotNet @(
        'run', '--project', 'compiler/src/Asn1Kit.Cli', '--', 'generate',
        '-i', $temporaryIr,
        '--patch', 'compiler/fixtures/ir/dvcs.patch.json',
        '--lang', 'csharp',
        '-o', 'runtime-csharp/generated/Asn1Kit.Pkix'
    )
}
finally {
    Pop-Location
    if (Test-Path -LiteralPath $temporaryIr) {
        Remove-Item -LiteralPath $temporaryIr -Force
    }
}
