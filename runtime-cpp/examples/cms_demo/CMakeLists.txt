# Copyright Asn1Kit contributors
# SPDX-License-Identifier: Apache-2.0

add_executable(asn1kit_cms_demo
    main.cpp
    format.cpp
    certificate_inspect.cpp
    cms_inspect.cpp
    ocsp_demo.cpp
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

set(ASN1KIT_PKIX_FIXTURES_DIR
    "${CMAKE_CURRENT_SOURCE_DIR}/../../../runtime-csharp/fixtures/pkix"
    CACHE PATH "Path to runtime-csharp PKIX fixtures")
file(TO_CMAKE_PATH "${ASN1KIT_PKIX_FIXTURES_DIR}" _asn1kit_pkix_fixtures_dir)

add_test(
    NAME cms_demo_verify_smoke
    COMMAND asn1kit_cms_demo verify "${_asn1kit_cms_fixtures_dir}/attached-signeddata.p7m"
)

add_test(
    NAME cms_demo_print_cert_smoke
    COMMAND asn1kit_cms_demo print-cert "${_asn1kit_pkix_fixtures_dir}/GoodCACert.crt"
)

add_test(
    NAME cms_demo_ocsp_roundtrip_smoke
    COMMAND ${CMAKE_COMMAND}
        -DDEMO=$<TARGET_FILE:asn1kit_cms_demo>
        -DEE=${_asn1kit_pkix_fixtures_dir}/ValidCertificatePathTest1EE.crt
        -DISSUER=${_asn1kit_pkix_fixtures_dir}/GoodCACert.crt
        -DOUT_DIR=${CMAKE_CURRENT_BINARY_DIR}/ocsp-smoke
        -P ${CMAKE_CURRENT_SOURCE_DIR}/ocsp_roundtrip_smoke.cmake
)
