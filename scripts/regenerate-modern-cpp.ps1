# Regenerates committed C++ codegen golden for modern-pkix-cms.json
# (24 modules + all.hpp + certificate.hpp) under runtime-cpp/generated/modern/.
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$patchPath = Join-Path ([IO.Path]::GetTempPath()) ('asn1kit-modern-cpp-' + [guid]::NewGuid().ToString('N') + '.json')

function Invoke-DotNet {
    param([string[]] $Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE." }
}

Push-Location $repositoryRoot
try {
    $modules = [ordered]@{}
    $inputPaths = [string[]]@(Get-ChildItem -LiteralPath 'compiler/fixtures/asn1/modern' -Filter '*.asn' | ForEach-Object { $_.FullName })
    [Array]::Sort($inputPaths, [StringComparer]::Ordinal)
    foreach ($path in $inputPaths) {
        $name = [IO.Path]::GetFileNameWithoutExtension($path)
        $identifier = -join ($name -split '[-_]' | ForEach-Object {
            $_.Substring(0, 1).ToUpperInvariant() + $_.Substring(1)
        })
        $modules[$name] = @{ cpp = @{ namespace = 'asn1kit::modern::' + $identifier } }
    }

    [IO.File]::WriteAllText(
        $patchPath,
        (@{ modules = $modules } | ConvertTo-Json -Depth 6),
        (New-Object Text.UTF8Encoding($false)))

    $out = 'runtime-cpp/generated/modern'
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    Get-ChildItem -LiteralPath $out -Filter '*.hpp' -ErrorAction SilentlyContinue | Remove-Item -Force

    Invoke-DotNet @(
        'run', '--project', 'compiler/src/Asn1Kit.Cli', '--', 'generate',
        '-i', 'compiler/fixtures/ir/modern-pkix-cms.json',
        '--lang', 'cpp',
        '--patch', $patchPath,
        '--patch', 'compiler/fixtures/ir/modern-pkix-cms.patch.json',
        '-o', $out
    )

    Write-Host "Updated $out"
}
finally {
    Pop-Location
    if (Test-Path -LiteralPath $patchPath) { Remove-Item -LiteralPath $patchPath -Force }
}
