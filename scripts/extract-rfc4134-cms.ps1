# Copyright Asn1Kit contributors
# SPDX-License-Identifier: Apache-2.0
#
# Extract CMS sample binaries from RFC 4134 Appendix A (|-prefixed Base64 blocks).
# Default source: runtime-csharp/fixtures/cms/rfc4134/rfc4134.txt
# Default output: runtime-csharp/fixtures/cms/rfc4134/

[CmdletBinding()]
param(
    [string] $RfcPath,
    [string] $OutputDirectory,
    [string[]] $Names = @(
        '3.2.bin',
        '4.2.bin',
        '4.3.bin',
        '4.6.bin',
        '4.7.bin',
        '4.10.bin',
        'ExContent.bin'
    )
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $RfcPath) {
    $RfcPath = Join-Path $repositoryRoot 'runtime-csharp/fixtures/cms/rfc4134/rfc4134.txt'
}
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repositoryRoot 'runtime-csharp/fixtures/cms/rfc4134'
}

if (!(Test-Path -LiteralPath $RfcPath)) {
    throw "RFC text not found: $RfcPath"
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$wanted = [System.Collections.Generic.HashSet[string]]::new(
    [string[]]$Names,
    [System.StringComparer]::OrdinalIgnoreCase)

$currentName = $null
$base64 = [System.Text.StringBuilder]::new()
$extracted = @{}

foreach ($raw in Get-Content -LiteralPath $RfcPath -Encoding UTF8) {
    if ($raw.Length -eq 0 -or $raw[0] -ne '|') { continue }
    $line = $raw.Substring(1)
    if ($line.StartsWith('*')) { continue }

    if ($line.StartsWith('>')) {
        $currentName = $line.Substring(1).Trim()
        [void]$base64.Clear()
        continue
    }

    if ($line.StartsWith('<')) {
        $endName = $line.Substring(1).Trim()
        if ($null -eq $currentName) {
            throw "Closing marker '</$endName' without open block."
        }
        if ($endName -ne $currentName) {
            throw "Mismatched block markers: opened '$currentName', closed '$endName'."
        }

        if ($wanted.Contains($currentName)) {
            $bytes = [Convert]::FromBase64String($base64.ToString())
            $outPath = Join-Path $OutputDirectory $currentName
            [System.IO.File]::WriteAllBytes($outPath, $bytes)
            $extracted[$currentName] = $bytes.Length
            Write-Output ("{0}: {1} bytes" -f $currentName, $bytes.Length)
        }

        $currentName = $null
        [void]$base64.Clear()
        continue
    }

    if ($null -ne $currentName) {
        [void]$base64.Append($line.Trim())
    }
}

$missing = @($Names | Where-Object { -not $extracted.ContainsKey($_) })
if ($missing.Count -gt 0) {
    throw ("Missing RFC 4134 blocks: {0}" -f ($missing -join ', '))
}

# Plan fixture name for detached SignedData external content.
$exContent = Join-Path $OutputDirectory 'ExContent.bin'
$detached = Join-Path $OutputDirectory 'detached-content.bin'
Copy-Item -LiteralPath $exContent -Destination $detached -Force
Write-Output ("detached-content.bin: copy of ExContent.bin ({0} bytes)" -f (Get-Item -LiteralPath $detached).Length)
