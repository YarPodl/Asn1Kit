# Applies the curated Modern IMPORTS / type-form patch to RFC 3029 Appendix E.
# Source: compiler/fixtures/asn1/modern/sources/PKIXDVCS.rfc3029.asn
# Output: compiler/fixtures/asn1/modern/PKIXDVCS-2009.asn

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$sourcePath = Join-Path $repositoryRoot 'compiler/fixtures/asn1/modern/sources/PKIXDVCS.rfc3029.asn'
$destinationPath = Join-Path $repositoryRoot 'compiler/fixtures/asn1/modern/PKIXDVCS-2009.asn'

if (!(Test-Path -LiteralPath $sourcePath)) {
    throw "Missing DVCS source: $sourcePath"
}

$body = [IO.File]::ReadAllText($sourcePath)

# Drop the source-only provenance lines; the output header documents curation.
$body = [regex]::Replace($body, '(?m)^-- RFC 3029 Appendix E.*\r?\n', '')
$body = [regex]::Replace($body, '(?m)^-- Applies verified errata.*\r?\n', '')
$body = [regex]::Replace($body, '(?m)^-- Normalizes CMS/X\.509 module names.*\r?\n', '')
$body = [regex]::Replace($body, '(?m)^-- CertId/ESSCertId spellings.*\r?\n', '')
$body = [regex]::Replace($body, '(?m)^-- Copyright \(c\) The IETF Trust.*\r?\n', '')
$body = [regex]::Replace($body, '(?m)^-- See \.\./LICENSE\.txt.*\r?\n', '')
$body = [regex]::Replace($body, '(?m)^-- Curated Modern corpus file is produced by.*\r?\n', '')
$body = $body.TrimStart()

$legacyImports = @'
IMPORTS
  Extensions, Extension, AlgorithmIdentifier, Certificate, CertificateList
  FROM PKIX1Explicit88 {iso(1) identified-organization(3)
  dod(6) internet(1) security(5) mechanisms(5) pkix(7)
  id-mod(0) id-pkix1-explicit-88(1)}
  GeneralName, GeneralNames, PolicyInformation
  FROM PKIX1Implicit88 {iso(1) identified-organization(3)
  dod(6) internet(1) security(5) mechanisms(5) pkix(7)
  id-mod(0) id-pkix1-implicit-88(2)}
  PKIStatusInfo FROM PKIXCMP {iso(1)
  identified-organization(3) dod(6) internet(1) security(5)
  mechanisms(5) pkix(7) id-mod(0)
  id-mod-cmp(9)}
  ContentInfo, DigestAlgorithmIdentifier, SignerInfos
  FROM CryptographicMessageSyntax2004 {iso(1)
  member-body(2) us(840) rsadsi(113549) pkcs(1) pkcs-9(9)
  smime(16) modules(0) cms-2004(24)}
  ESSCertID FROM ExtendedSecurityServices
  { iso(1) member-body(2) us(840) rsadsi(113549)
  pkcs(1) pkcs-9(9) smime(16) modules(0) ess(2) }
  CertID, OCSPResponse, CertStatus
  FROM OCSP
  {iso(1) identified-organization(3)
  dod(6) internet(1) security(5) mechanisms(5) pkix(7) id-mod(0)
  id-mod-ocsp(14)}
  SMIMECapabilities FROM SecureMimeMessageV3
  { iso(1) member-body(2) us(840) rsadsi(113549)
   pkcs(1) pkcs-9(9) smime(16) modules(0) smime(4) }
  ;
'@

$modernImports = @'
IMPORTS

  Extensions{}, Extension{}
  FROM PKIX-CommonTypes-2009
      {iso(1) identified-organization(3) dod(6) internet(1) security(5)
      mechanisms(5) pkix(7) id-mod(0) id-mod-pkixCommon-02(57)}

  Certificate, CertificateList
  FROM PKIX1Explicit-2009
      {iso(1) identified-organization(3) dod(6) internet(1) security(5)
      mechanisms(5) pkix(7) id-mod(0) id-mod-pkix1-explicit-02(51)}

  GeneralName, GeneralNames, PolicyInformation
  FROM PKIX1Implicit-2009
      {iso(1) identified-organization(3) dod(6) internet(1) security(5)
      mechanisms(5) pkix(7) id-mod(0) id-mod-pkix1-implicit-02(59)}

  PKIStatusInfo
  FROM PKIXCMP-2009
      { iso(1) identified-organization(3) dod(6) internet(1) security(5)
      mechanisms(5) pkix(7) id-mod(0) id-mod-cmp2000-02(50)}

  ContentInfo, DigestAlgorithmIdentifier, SignerInfos
  FROM CryptographicMessageSyntax-2009
      { iso(1) member-body(2) us(840) rsadsi(113549)
      pkcs(1) pkcs-9(9) smime(16) modules(0) id-mod-cms-2004-02(41)}

  ESSCertID
  FROM ExtendedSecurityServices-2009
      { iso(1) member-body(2) us(840) rsadsi(113549) pkcs(1) pkcs-9(9)
      smime(16) modules(0) id-mod-ess-2006-02(42) }

  CertID, OCSPResponse, CertStatus
  FROM OCSP-2009
      {iso(1) identified-organization(3) dod(6) internet(1) security(5)
      mechanisms(5) pkix(7) id-mod(0) id-mod-ocsp-02(48)}

  SMIMECapabilities{}
  FROM AlgorithmInformation-2009
      {iso(1) identified-organization(3) dod(6) internet(1) security(5)
      mechanisms(5) pkix(7) id-mod(0) id-mod-algorithmInformation-02(58)} ;
'@

if (!$body.Contains($legacyImports)) {
    throw 'Expected legacy IMPORTS block not found in PKIXDVCS.rfc3029.asn.'
}

$body = $body.Replace($legacyImports, $modernImports)

# Modern has no bare Extensions / Extension / SMIMECapabilities typedefs.
$body = $body.Replace(
    '     capabilities                 [8] SMIMECapabilities,',
    '     capabilities                 [8] SMIMECapabilities{{...}},')
$body = $body.Replace(
    '     extension                    Extension',
    '     extension                    Extension{{...}}')
$body = $body.Replace(
    '        extensions                   [4] IMPLICIT Extensions OPTIONAL',
    '        extensions                   [4] IMPLICIT Extensions{{...}} OPTIONAL')
$body = $body.Replace(
    '         extensions          Extensions OPTIONAL',
    '         extensions          Extensions{{...}} OPTIONAL')

# Rename the ASN.1 module for the Modern corpus filename convention; keep id-mod-dvcs(15).
$body = [regex]::Replace($body, '(?m)^PKIXDVCS\b', 'PKIXDVCS-2009')

$header = @"
-- Curated from RFC 3029 Appendix E; ASN.1 syntax retained.
-- IMPORTS retargeted to Modern PKIX/CMS/CMP/OCSP/ESS modules; bare
-- Extensions/Extension/SMIMECapabilities -> parameterized {{...}} forms.
-- Source: sources/PKIXDVCS.rfc3029.asn; apply via scripts/patch-pkixdvcs-modern.ps1.
-- Copyright (c) The IETF Trust and the persons identified as authors.
-- See LICENSE.txt for the Simplified BSD license.

"@

$text = $header + $body
if (!$text.EndsWith("`n")) { $text += "`n" }
[IO.File]::WriteAllText($destinationPath, $text, (New-Object Text.UTF8Encoding($false)))
Write-Output "Wrote $destinationPath"
