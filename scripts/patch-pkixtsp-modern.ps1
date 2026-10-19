# Applies the curated Modern IMPORTS / type-form patch to RFC 3161 Appendix C.
# Source: compiler/fixtures/asn1/modern/sources/PKIXTSP.rfc3161.asn
# Output: compiler/fixtures/asn1/modern/PKIXTSP-2009.asn

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$sourcePath = Join-Path $repositoryRoot 'compiler/fixtures/asn1/modern/sources/PKIXTSP.rfc3161.asn'
$destinationPath = Join-Path $repositoryRoot 'compiler/fixtures/asn1/modern/PKIXTSP-2009.asn'

if (!(Test-Path -LiteralPath $sourcePath)) {
    throw "Missing TSP source: $sourcePath"
}

$body = [IO.File]::ReadAllText($sourcePath)

# Drop the source-only provenance lines; the output header documents curation.
$body = [regex]::Replace($body, '(?m)^-- RFC 3161 Appendix C.*\r?\n', '')
$body = [regex]::Replace($body, '(?m)^-- Copyright \(c\) The IETF Trust.*\r?\n', '')
$body = [regex]::Replace($body, '(?m)^-- See \.\./LICENSE\.txt.*\r?\n', '')
$body = [regex]::Replace($body, '(?m)^-- Curated Modern corpus file is produced by.*\r?\n', '')
$body = $body.TrimStart()

$legacyImports = @'
IMPORTS

     Extensions, AlgorithmIdentifier
     FROM PKIX1Explicit88 {iso(1) identified-organization(3)
     dod(6) internet(1) security(5) mechanisms(5) pkix(7)
     id-mod(0) id-pkix1-explicit-88(1)}

     GeneralName FROM PKIX1Implicit88 {iso(1)
     identified-organization(3) dod(6) internet(1) security(5)
     mechanisms(5) pkix(7) id-mod(0) id-pkix1-implicit-88(2)}

     ContentInfo FROM CryptographicMessageSyntax {iso(1)
     member-body(2) us(840) rsadsi(113549) pkcs(1) pkcs-9(9)
     smime(16) modules(0) cms(1)}

     PKIFreeText FROM PKIXCMP {iso(1) identified-organization(3)
     dod(6) internet(1) security(5) mechanisms(5) pkix(7) id-mod(0)
     id-mod-cmp(9)} ;
'@

$modernImports = @'
IMPORTS

  Extensions{}
  FROM PKIX-CommonTypes-2009
      {iso(1) identified-organization(3) dod(6) internet(1) security(5)
      mechanisms(5) pkix(7) id-mod(0) id-mod-pkixCommon-02(57)}

  GeneralName
  FROM PKIX1Implicit-2009
      {iso(1) identified-organization(3) dod(6) internet(1) security(5)
      mechanisms(5) pkix(7) id-mod(0) id-mod-pkix1-implicit-02(59)}

  ContentInfo, DigestAlgorithmIdentifier
  FROM CryptographicMessageSyntax-2009
      { iso(1) member-body(2) us(840) rsadsi(113549)
      pkcs(1) pkcs-9(9) smime(16) modules(0) id-mod-cms-2004-02(41)}

  PKIFreeText
  FROM PKIXCMP-2009
      { iso(1) identified-organization(3) dod(6) internet(1) security(5)
      mechanisms(5) pkix(7) id-mod(0) id-mod-cmp2000-02(50)} ;
'@

if (!$body.Contains($legacyImports)) {
    throw 'Expected legacy IMPORTS block not found in PKIXTSP.rfc3161.asn.'
}

$body = $body.Replace($legacyImports, $modernImports)

# Modern has no bare AlgorithmIdentifier / Extensions typedef; match OCSP/CMP forms.
$body = $body.Replace(
    '     hashAlgorithm                AlgorithmIdentifier,',
    '     hashAlgorithm                DigestAlgorithmIdentifier,')
$body = $body.Replace(
    '   extensions               [0] IMPLICIT Extensions    OPTIONAL  }',
    '   extensions               [0] IMPLICIT Extensions{{...}} OPTIONAL  }')
$body = $body.Replace(
    '    extensions                   [1] IMPLICIT Extensions  OPTIONAL   }',
    '    extensions                   [1] IMPLICIT Extensions{{...}} OPTIONAL   }')

# Rename the ASN.1 module for the Modern corpus filename convention; keep id-mod-tsp(13).
$body = [regex]::Replace($body, '(?m)^PKIXTSP\b', 'PKIXTSP-2009')

# RFC pagination left un-commented continuations inside INTEGER/BIT STRING braces.
$body = $body.Replace(
    "    -- when the PKIStatus contains the value zero a TimeStampToken, as`r`n       requested, is present.",
    "    -- when the PKIStatus contains the value zero a TimeStampToken, as`n    -- requested, is present.")
$body = $body.Replace(
    "    -- when the PKIStatus contains the value zero a TimeStampToken, as`n       requested, is present.",
    "    -- when the PKIStatus contains the value zero a TimeStampToken, as`n    -- requested, is present.")
$body = $body.Replace(
    "     -- when the PKIStatus contains the value one a TimeStampToken,`r`n       with modifications, is present.",
    "    -- when the PKIStatus contains the value one a TimeStampToken,`n    -- with modifications, is present.")
$body = $body.Replace(
    "     -- when the PKIStatus contains the value one a TimeStampToken,`n       with modifications, is present.",
    "    -- when the PKIStatus contains the value one a TimeStampToken,`n    -- with modifications, is present.")

# Closing braces were swallowed into trailing RFC comments.
$body = $body.Replace(
    '     -- notification that a revocation has occurred   }',
    "    -- notification that a revocation has occurred`n}")
$body = $body.Replace(
    '      -- the request cannot be handled due to system failure  }',
    "      -- the request cannot be handled due to system failure`n}")

# RFC omitted the comma before the last named bit.
$body = $body.Replace(
    '    addInfoNotAvailable (17)',
    '    addInfoNotAvailable (17),')

$header = @"
-- Curated from RFC 3161 Appendix C; ASN.1 syntax retained.
-- IMPORTS retargeted to Modern PKIX/CMS/CMP modules; AlgorithmIdentifier ->
-- DigestAlgorithmIdentifier; Extensions -> Extensions{{...}}.
-- Source: sources/PKIXTSP.rfc3161.asn; apply via scripts/patch-pkixtsp-modern.ps1.
-- Copyright (c) The IETF Trust and the persons identified as authors.
-- See LICENSE.txt for the Simplified BSD license.

"@

$text = $header + $body
if (!$text.EndsWith("`n")) { $text += "`n" }
[IO.File]::WriteAllText($destinationPath, $text, (New-Object Text.UTF8Encoding($false)))
Write-Output "Wrote $destinationPath"
