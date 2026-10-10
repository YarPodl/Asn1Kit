# Copyright Asn1Kit contributors
# SPDX-License-Identifier: Apache-2.0

add_executable(asn1kit_tests
    ber_der_fixtures.cpp
    primitive_codec_tests.cpp
    bytes_tests.cpp
)

target_link_libraries(asn1kit_tests
    PRIVATE
        asn1kit::asn1kit
        GTest::gtest
        GTest::gtest_main
        nlohmann_json::nlohmann_json
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
