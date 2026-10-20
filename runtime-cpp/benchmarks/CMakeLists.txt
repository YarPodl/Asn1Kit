# Copyright Asn1Kit contributors
# SPDX-License-Identifier: Apache-2.0

add_executable(asn1kit_benchmarks
    main.cpp
    fixture_loader.cpp
    openssl_support.cpp
    smoke.cpp
    encode_samples.cpp
    certificate_bench.cpp
    certificate_list_bench.cpp
    cms_bench.cpp
)

target_link_libraries(asn1kit_benchmarks
    PRIVATE
        asn1kit::asn1kit
        benchmark::benchmark
        OpenSSL::Crypto
)

target_include_directories(asn1kit_benchmarks
    PRIVATE
        "${CMAKE_CURRENT_SOURCE_DIR}"
        "${CMAKE_CURRENT_SOURCE_DIR}/../generated/modern"
)

set(ASN1KIT_PKIX_FIXTURES_DIR
    "${CMAKE_CURRENT_SOURCE_DIR}/../../runtime-csharp/fixtures/pkix"
    CACHE PATH "Path to runtime-csharp pkix certificate fixtures")
set(ASN1KIT_CMS_FIXTURES_DIR
    "${CMAKE_CURRENT_SOURCE_DIR}/../../runtime-csharp/fixtures/cms"
    CACHE PATH "Path to runtime-csharp cms fixtures")

file(TO_CMAKE_PATH "${ASN1KIT_PKIX_FIXTURES_DIR}" _asn1kit_pkix_fixtures_dir)
file(TO_CMAKE_PATH "${ASN1KIT_CMS_FIXTURES_DIR}" _asn1kit_cms_fixtures_dir)
target_compile_definitions(asn1kit_benchmarks
    PRIVATE
        "ASN1KIT_PKIX_FIXTURES_DIR=\"${_asn1kit_pkix_fixtures_dir}\""
        "ASN1KIT_CMS_FIXTURES_DIR=\"${_asn1kit_cms_fixtures_dir}\""
)

if(MSVC)
    target_compile_options(asn1kit_benchmarks PRIVATE /W4 /permissive- /utf-8 /bigobj)
else()
    target_compile_options(asn1kit_benchmarks PRIVATE -Wall -Wextra -Wpedantic)
endif()
