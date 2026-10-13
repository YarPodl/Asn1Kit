# Copyright Asn1Kit contributors
# SPDX-License-Identifier: Apache-2.0

add_executable(asn1kit_tests
    ber_der_fixtures.cpp
    primitive_codec_tests.cpp
    oid_literal_tests.cpp
    bytes_tests.cpp
    exception_diagnostics_tests.cpp
    constructed_scope_tests.cpp
    generated_person_tests.cpp
    generated_extensible_tests.cpp
    tag_tests.cpp
    any_tests.cpp
    contained_tests.cpp
    codec_tests.cpp
    open_binding_tests.cpp
    value_lazy_tests.cpp
    generated_deferred_tests.cpp
    utc_date_time_tests.cpp
    encode_decode_tests.cpp
    format_tests.cpp
)

target_link_libraries(asn1kit_tests
    PRIVATE
        asn1kit::asn1kit
        GTest::gtest
        GTest::gtest_main
        nlohmann_json::nlohmann_json
)

target_include_directories(asn1kit_tests
    PRIVATE
        "${CMAKE_CURRENT_SOURCE_DIR}/../generated/example"
        "${CMAKE_CURRENT_SOURCE_DIR}/../generated/open-binding"
        "${CMAKE_CURRENT_SOURCE_DIR}/../generated/extensible"
        "${CMAKE_CURRENT_SOURCE_DIR}/../generated/deferred"
)

# Prefer forward slashes so the string macro is portable on Windows and Linux.
file(TO_CMAKE_PATH "${ASN1KIT_BER_DER_FIXTURES_DIR}" _asn1kit_fixtures_dir)
target_compile_definitions(asn1kit_tests
    PRIVATE
        "ASN1KIT_BER_DER_FIXTURES_DIR=\"${_asn1kit_fixtures_dir}\""
)

if(MSVC)
    target_compile_options(asn1kit_tests PRIVATE /W4 /permissive- /utf-8)
else()
    target_compile_options(asn1kit_tests PRIVATE -Wall -Wextra -Wpedantic)
endif()

include(GoogleTest)
gtest_discover_tests(asn1kit_tests)

# Modern PKIX/CMS amalgam is large; keep Certificate round-trip in a separate binary.
add_executable(asn1kit_modern_tests
    generated_modern_certificate_tests.cpp
)

target_link_libraries(asn1kit_modern_tests
    PRIVATE
        asn1kit::asn1kit
        GTest::gtest
        GTest::gtest_main
)

target_include_directories(asn1kit_modern_tests
    PRIVATE
        "${CMAKE_CURRENT_SOURCE_DIR}/../generated/modern"
)

set(ASN1KIT_PKIX_FIXTURES_DIR
    "${CMAKE_CURRENT_SOURCE_DIR}/../../runtime-csharp/fixtures/pkix"
    CACHE PATH "Path to runtime-csharp pkix certificate fixtures")
file(TO_CMAKE_PATH "${ASN1KIT_PKIX_FIXTURES_DIR}" _asn1kit_pkix_fixtures_dir)
target_compile_definitions(asn1kit_modern_tests
    PRIVATE
        "ASN1KIT_PKIX_FIXTURES_DIR=\"${_asn1kit_pkix_fixtures_dir}\""
)

if(MSVC)
    target_compile_options(asn1kit_modern_tests PRIVATE /W4 /permissive- /utf-8 /bigobj)
else()
    target_compile_options(asn1kit_modern_tests PRIVATE -Wall -Wextra -Wpedantic)
endif()

gtest_discover_tests(asn1kit_modern_tests)
