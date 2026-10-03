[CmdletBinding()]
param([string] $InputDirectory = '.cache/rfc')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$destination = Join-Path $repositoryRoot 'compiler/fixtures/asn1/modern'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
$seen = @{}
foreach ($rfcNumber in @(5912, 5911, 6268, 8410)) {
    $sourcePath = Join-Path $InputDirectory ('rfc' + $rfcNumber + '.txt')
    $lines = @(Get-Content -Encoding UTF8 -LiteralPath $sourcePath | Where-Object {
        $_ -notmatch '^\s*RFC\s+\d+' -and
        $_ -notmatch '\[Page \d+\]' -and
        $_ -notmatch '^\s*\f\s*$'
    })
    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -notmatch '^\s*DEFINITIONS\s+(EXPLICIT|IMPLICIT|AUTOMATIC)\s+TAGS\s*::=') { continue }
        $start = $index - 1
        while ($start -ge 0 -and $lines[$start] -notmatch '^\s*([A-Za-z][A-Za-z0-9-]*)\s*(\{|$)') { $start-- }
        if ($start -lt 0) { throw "Module header not found in RFC $rfcNumber." }
        $name = $Matches[1]
        $end = $index + 1
        while ($end -lt $lines.Count -and $lines[$end] -notmatch '^\s*END\s*$') { $end++ }
        if ($end -eq $lines.Count) { throw "Module END not found for $name." }
        if (!$seen.ContainsKey($name)) {
            $body = ($lines[$start..$end] -join "`n").TrimEnd() + "`n"
            # Verified EID 2612 (RFC 5911), EID 3130 (RFC 5912): CMS id-data has no ASN.1 content type.
            $body = $body.Replace('CONTENT-TYPE ::= TYPE-IDENTIFIER', "CONTENT-TYPE ::= CLASS { &id OBJECT IDENTIFIER UNIQUE, &Type OPTIONAL }`n    WITH SYNTAX { [TYPE &Type] IDENTIFIED BY &id }")
            $body = [regex]::Replace($body, '(ct-Data\s+CONTENT-TYPE\s*::=\s*\{\s*)OCTET STRING\s+', '${1}')
            $body = [regex]::Replace($body, '(\b[a-z][A-Za-z0-9-]*\s+CONTENT-TYPE\s*::=\s*\{\s*)(?!TYPE\b|IDENTIFIED\b)(?=[A-Z])', '${1}TYPE ')
            if ($name -eq 'ERS') {
                # Verified EID 3128 (RFC 5911).
                $body = $body.Replace('CryptographicMessageSyntax2004', 'CryptographicMessageSyntax-2009')
            }
            if ($name -eq 'OCSP-2009') {
                # Verified EID 3259 (RFC 5912): CRLReason is ENUMERATED, not INTEGER.
                $body = $body.Replace('AuthorityInfoAccessSyntax, GeneralName, CrlEntryExtensions', 'CRLReason, AuthorityInfoAccessSyntax, GeneralName, CrlEntryExtensions')
                $body = [regex]::Replace($body, '(?m)^\s*CRLReason ::= INTEGER\s*$', '')
            }
            if ($name -eq 'CMS-AES-CCM-and-AES-GCM-2009') {
                # Local correction of two copy/paste identifier errors; RFC 5084 defines distinct GCM OIDs.
                foreach ($bits in @(192, 256)) {
                    $pattern = '(cea-aes' + $bits + '-GCM CONTENT-ENCRYPTION ::= \{\s+IDENTIFIER )id-aes128-GCM'
                    $body = [regex]::Replace($body, $pattern, ('${1}id-aes' + $bits + '-GCM'))
                }
            }
            $header = "-- Extracted from RFC $rfcNumber; ASN.1 syntax retained.`n-- Copyright (c) The IETF Trust and the persons identified as authors.`n-- See LICENSE.txt for the Simplified BSD license.`n`n"
            [IO.File]::WriteAllText((Join-Path $destination ($name + '.asn')), $header + $body, (New-Object Text.UTF8Encoding($false)))
            $seen[$name] = $rfcNumber
            Write-Output "$name (RFC $rfcNumber)"
        }
        $index = $end
    }
}
