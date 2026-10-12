# Copyright Asn1Kit contributors
# SPDX-License-Identifier: Apache-2.0

add_executable(asn1kit_cms_demo
    main.cpp
    cms_demo.cpp
    sha256.cpp
)

target_link_libraries(asn1kit_cms_demo PRIVATE asn1kit::asn1kit)

target_include_directories(asn1kit_cms_demo PRIVATE
    "${CMAKE_CURRENT_SOURCE_DIR}/../../generated/modern"
)

if(MSVC)
    target_compile_options(asn1kit_cms_demo PRIVATE /W4 /permissive- /utf-8 /bigobj)
else()
    target_compile_options(asn1kit_cms_demo PRIVATE -Wall -Wextra -Wpedantic)
endif()

set(ASN1KIT_CMS_FIXTURES_DIR
    "${CMAKE_CURRENT_SOURCE_DIR}/../../../runtime-csharp/fixtures/cms"
    CACHE PATH "Path to runtime-csharp CMS fixtures")
file(TO_CMAKE_PATH "${ASN1KIT_CMS_FIXTURES_DIR}" _asn1kit_cms_fixtures_dir)

add_test(
    NAME cms_demo_verify_smoke
    COMMAND asn1kit_cms_demo verify "${_asn1kit_cms_fixtures_dir}/attached-signeddata.p7m"
)
