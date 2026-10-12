# asn1kit_cms_demo

Консольная демонстрация **Modern** CMS-2009 / PKIX-2009 generated-типов (`runtime-cpp/generated/modern/`): `verify`, `print-cert`, `print-cms`, `sign`. Криптопроверка — educational stub; `sign` пишет placeholder-подпись и реальный SHA-256 в `messageDigest`.

## Сборка

Из `runtime-cpp/` (preset `fetch-default` или `conan-*`):

```powershell
cmake --preset fetch-default
cmake --build --preset fetch-default --target asn1kit_cms_demo
```

Опция CMake: `ASN1KIT_BUILD_EXAMPLES` (по умолчанию `ON`).

## Запуск

```powershell
.\build-fetch\examples\cms_demo\asn1kit_cms_demo.exe verify ..\runtime-csharp\fixtures\cms\attached-signeddata.p7m
.\build-fetch\examples\cms_demo\asn1kit_cms_demo.exe print-cert ..\runtime-csharp\fixtures\pkix\GoodCACert.crt
.\build-fetch\examples\cms_demo\asn1kit_cms_demo.exe print-cms ..\runtime-csharp\fixtures\cms\attached-signeddata.p7m
.\build-fetch\examples\cms_demo\asn1kit_cms_demo.exe sign --certificate path\to\cert.cer --content path\to\data.bin --output out.p7m
```

Smoke: `ctest -R cms_demo_verify_smoke` (после сборки того же preset).
