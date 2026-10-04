[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$patchPath = Join-Path ([IO.Path]::GetTempPath()) ('asn1kit-modern-' + [guid]::NewGuid().ToString('N') + '.json')
function Invoke-DotNet {
    param([string[]] $Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE." }
}
Push-Location $repositoryRoot
try {
    $modules = [ordered]@{}
    $arguments = @('run', '--project', 'compiler/src/Asn1Kit.Cli', '--', 'compile')
    $inputPaths = [string[]]@(Get-ChildItem -LiteralPath 'compiler/fixtures/asn1/modern' -Filter '*.asn' | ForEach-Object { $_.FullName })
    [Array]::Sort($inputPaths, [StringComparer]::Ordinal)
    foreach ($path in $inputPaths) {
        $file = Get-Item -LiteralPath $path
        $arguments += @('-i', $file.FullName)
        $name = $file.BaseName
        $identifier = -join ($name -split '[-_]' | ForEach-Object { $_.Substring(0, 1).ToUpperInvariant() + $_.Substring(1) })
        $modules[$name] = @{ csharp = @{ namespace = 'Asn1Kit.Modern.' + $identifier } }
    }
    [IO.File]::WriteAllText($patchPath, (@{ modules = $modules } | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding($false)))
    $arguments += @('--patch', $patchPath, '--patch', 'compiler/fixtures/ir/modern-pkix-cms.patch.json',
        '-o', 'compiler/fixtures/ir/modern-pkix-cms.json')
    Invoke-DotNet $arguments
    Invoke-DotNet @('run', '--project', 'compiler/src/Asn1Kit.Cli', '--', 'generate',
        '-i', 'compiler/fixtures/ir/modern-pkix-cms.json', '--lang', 'csharp', '-o', 'runtime-csharp/generated/Asn1Kit.Modern')
}
finally {
    Pop-Location
    if (Test-Path -LiteralPath $patchPath) { Remove-Item -LiteralPath $patchPath -Force }
}
