# C++ runtime (Asn1Kit)

Нативный BER/DER runtime на **C++20**: примитивы и публичные SEQUENCE/SET/OF/EXPLICIT scopes (`enter_*` / `with_*`). Ownership — `asn1kit::bytes` (`span` + keep-alive источника). Soft-profile и layer-1 hex-векторы общие с эталонным runtime ([`runtime-csharp/fixtures/ber-der`](../runtime-csharp/fixtures/ber-der/)).

**Windows и Linux — равноправные first-class платформы** (MSVC / Clang / GCC + Ninja).

Codegen C++ (`Asn1Kit.Codegen.Cpp`) — backlog; см. [docs/status.md](../docs/status.md).

## Зависимости

| Слой | Зависимости |
| --- | --- |
| Библиотека `asn1kit` | нет (только стандарт C++20) |
| Тесты | [GoogleTest](https://github.com/google/googletest), [nlohmann_json](https://github.com/nlohmann/json) — через **Conan 2** или **FetchContent** (не линкуются в `libasn1kit`) |

Фикстуры BER/DER **не копируются**: тесты читают `../runtime-csharp/fixtures/ber-der/*.json` через `ASN1KIT_BER_DER_FIXTURES_DIR`.

## Сборка

Требуется: CMake ≥ 3.21 (presets schema v3), C++20 (MSVC 2022 / Clang / GCC), Ninja.
На Windows удобен CMake/Ninja из VS:
`…\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin` и `…\CMake\Ninja`.

### Без Conan (FetchContent) — Windows / Linux

```powershell
cd runtime-cpp
cmake --preset fetch-default
cmake --build --preset fetch-default
ctest --preset fetch-default
```

Deps скачиваются в `build-fetch/_deps`. Нужен доступ к GitHub (git).

### Conan 2 — Windows (PowerShell)

```powershell
cd runtime-cpp
conan install . -s build_type=Debug -of build --build=missing
cmake --preset conan-default
cmake --build --preset conan-default
ctest --preset conan-default
```

Release (`-of` должен совпадать с `binaryDir` пресета):

```powershell
conan install . -s build_type=Release -of build-release --build=missing
cmake --preset conan-release
cmake --build --preset conan-release
ctest --preset conan-release
```

Visual Studio generator: пресет `conan-default-vs` после `conan install … -of build`.

Если системный диск почти заполнен, задайте кэш и TEMP на другой диск, например:
`$env:CONAN_HOME = "D:\conan-home"; $env:TEMP = "D:\Temp"; $env:TMP = "D:\Temp"`.

### Conan 2 — Linux

```bash
cd runtime-cpp
conan install . -s build_type=Debug -of build --build=missing
cmake --preset conan-default
cmake --build --preset conan-default
ctest --preset conan-default
```

### Опции CMake

| Опция | По умолчанию | Смысл |
| --- | --- | --- |
| `ASN1KIT_BUILD_TESTS` | `ON` | Сборка gtest-таргета |
| `ASN1KIT_FETCH_TEST_DEPS` | `OFF` | FetchContent вместо `find_package` (пресет `fetch-default`) |
| `ASN1KIT_BER_DER_FIXTURES_DIR` | `../runtime-csharp/fixtures/ber-der` | Каталог JSON-фикстур |

Без тестов: `-DASN1KIT_BUILD_TESTS=OFF`.

## API

Краткий контракт — [docs/runtime-api.md](docs/runtime-api.md). Поведенческий эталон — [runtime-csharp/docs/runtime-api.md](../runtime-csharp/docs/runtime-api.md) и playbook [runtime.md](../runtime-csharp/docs/playbooks/runtime.md).

## Лицензия

Apache-2.0 (как корень репозитория).
