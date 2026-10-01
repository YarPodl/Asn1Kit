[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot

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
        '-o', 'compiler/fixtures/ir/pkix1-explicit88.json',
        '--bindings', 'compiler/fixtures/opentype/pkix-bindings.json'
    )

    Invoke-DotNet @(
        'run', '--project', 'compiler/src/Asn1Kit.Cli', '--', 'compile',
        '-i', 'compiler/fixtures/asn1/pkix1-explicit88.asn',
        '-i', 'compiler/fixtures/asn1/pkix1-implicit88.asn',
        '-o', 'compiler/fixtures/ir/pkix1-implicit88.json',
        '--bindings', 'compiler/fixtures/opentype/pkix-bindings.json'
    )

    $cmsPath = 'compiler/fixtures/ir/cms-2004.json'
    Invoke-DotNet @(
        'run', '--project', 'compiler/src/Asn1Kit.Cli', '--', 'compile',
        '-i', 'compiler/fixtures/asn1/pkix1-explicit88.asn',
        '-i', 'compiler/fixtures/asn1/pkix1-implicit88.asn',
        '-i', 'compiler/fixtures/asn1/cms-2004.asn',
        '-o', $cmsPath,
        '--bindings', 'compiler/fixtures/opentype/pkix-bindings.json',
        '--bindings', 'compiler/fixtures/opentype/cms-bindings.json'
    )

    $utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)
    $cmsJson = [System.IO.File]::ReadAllText((Join-Path $repositoryRoot $cmsPath), $utf8WithoutBom)
    $namespaces = @{
        'PKIX1Explicit88' = 'Asn1Kit.Pkix'
        'PKIX1Implicit88' = 'Asn1Kit.Pkix'
        'CryptographicMessageSyntax2004' = 'Asn1Kit.Cms'
    }

    foreach ($entry in $namespaces.GetEnumerator()) {
        $source = '"namespace": "{0}"' -f $entry.Key
        $target = '"namespace": "{0}"' -f $entry.Value
        $occurrences = ([regex]::Matches($cmsJson, [regex]::Escape($source))).Count
        if ($occurrences -ne 1) {
            throw "Expected exactly one occurrence of '$source', found $occurrences."
        }

        $cmsJson = $cmsJson.Replace($source, $target)
    }

    [System.IO.File]::WriteAllText((Join-Path $repositoryRoot $cmsPath), $cmsJson, $utf8WithoutBom)
}
finally {
    Pop-Location
}
