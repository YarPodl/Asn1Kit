# Copyright Asn1Kit contributors
# SPDX-License-Identifier: Apache-2.0

# Optional libFuzzer harness. Requires Clang with -fsanitize=fuzzer support.
# Not linked into the default test gate.

if(NOT CMAKE_CXX_COMPILER_ID MATCHES "Clang")
    message(WARNING "ASN1KIT_BUILD_FUZZ is ON but the compiler is not Clang; fuzz target skipped.")
    return()
endif()

add_executable(asn1kit_decode_fuzz decode_fuzz.cpp)
target_link_libraries(asn1kit_decode_fuzz PRIVATE asn1kit::asn1kit)
target_compile_options(asn1kit_decode_fuzz PRIVATE -fsanitize=fuzzer,address -fno-omit-frame-pointer)
target_link_options(asn1kit_decode_fuzz PRIVATE -fsanitize=fuzzer,address)

set(ASN1KIT_FUZZ_CORPUS_DIR "${CMAKE_CURRENT_BINARY_DIR}/corpus")
file(MAKE_DIRECTORY "${ASN1KIT_FUZZ_CORPUS_DIR}")
file(GLOB _asn1kit_seed_files CONFIGURE_DEPENDS "${CMAKE_CURRENT_SOURCE_DIR}/corpus/*.bin")
foreach(_seed IN LISTS _asn1kit_seed_files)
    get_filename_component(_seed_name "${_seed}" NAME)
    configure_file("${_seed}" "${ASN1KIT_FUZZ_CORPUS_DIR}/${_seed_name}" COPYONLY)
endforeach()

add_custom_target(asn1kit_fuzz_run
    COMMAND $<TARGET_FILE:asn1kit_decode_fuzz> "${ASN1KIT_FUZZ_CORPUS_DIR}" -max_total_time=10 -max_len=4096
    DEPENDS asn1kit_decode_fuzz
    WORKING_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}"
    COMMENT "Run asn1kit_decode_fuzz for 10 seconds against the seeded corpus"
)
