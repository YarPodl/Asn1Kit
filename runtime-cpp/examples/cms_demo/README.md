# asn1kit_cms_demo

Консольная демонстрация **Modern** CMS-2009 / PKIX-2009 / OCSP-2009 generated-типов
(`runtime-cpp/generated/modern/`): проверка attached SignedData, печать сертификата и CMS
с typed extensions/attributes, educational формирование OCSP request/response.

Криптопроверка — educational stub; `sign` и `ocsp-respond` пишут placeholder-подпись.
`sign` кладёт реальный SHA-256 в `messageDigest` и `signingTime` в signedAttrs.

## Сборка

```powershell
cmake --preset fetch-default
cmake --build --preset fetch-default --target asn1kit_cms_demo
```

## CMS / сертификат

```powershell
.\build-fetch\examples\cms_demo\asn1kit_cms_demo.exe verify ..\runtime-csharp\fixtures\cms\attached-signeddata.p7m
.\build-fetch\examples\cms_demo\asn1kit_cms_demo.exe print-cert ..\runtime-csharp\fixtures\pkix\GoodCACert.crt
.\build-fetch\examples\cms_demo\asn1kit_cms_demo.exe print-cms ..\runtime-csharp\fixtures\cms\attached-signeddata.p7m
.\build-fetch\examples\cms_demo\asn1kit_cms_demo.exe sign --certificate path\to\cert.cer --content path\to\data.bin --output out.p7m
```

`print-cert` декодирует SKI/AKI/KeyUsage/BasicConstraints/CertificatePolicies/AIA
через `TBSCertificate::cert_extensions()`. `print-cms` раскрывает signedAttrs
(`contentType`, `messageDigest`, `signingTime`) и краткие extensions вложенных
сертификатов. `verify` матчит SID по issuerAndSerial и SubjectKeyIdentifier,
сверяет SHA-256 `messageDigest` с eContent (если digest = id-sha256) и проверяет
KeyUsage educationally.

## OCSP (educational round-trip)

```powershell
$demo = .\build-fetch\examples\cms_demo\asn1kit_cms_demo.exe
$ee = ..\runtime-csharp\fixtures\pkix\ValidCertificatePathTest1EE.crt
$ca = ..\runtime-csharp\fixtures\pkix\GoodCACert.crt

& $demo ocsp-request --certificate $ee --issuer $ca --output req.der
& $demo print-ocsp-request req.der
& $demo ocsp-respond --request req.der --output resp.der
& $demo print-ocsp-response resp.der
& $demo ocsp-check --request req.der --response resp.der
```

Сценарий использует open-type фабрики/views: `Extension::make_local_request_extensions().re_ocsp_nonce`,
`TBSRequest::local().re_ocsp_nonce`, `ResponseBytes::make_response_set().basic_response`.

## Smoke

После сборки того же preset:

```powershell
ctest --test-dir build-fetch -R cms_demo_ --output-on-failure
```

Тесты: `cms_demo_verify_smoke`, `cms_demo_print_cert_smoke`, `cms_demo_ocsp_roundtrip_smoke`.
