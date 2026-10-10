# Copyright Asn1Kit contributors
# SPDX-License-Identifier: Apache-2.0

cmake_minimum_required(VERSION 3.21)
project(asn1kit VERSION 0.1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

option(ASN1KIT_BUILD_TESTS "Build asn1kit unit tests" ON)
option(ASN1KIT_FETCH_TEST_DEPS
    "Fetch gtest and nlohmann_json via FetchContent instead of find_package (no Conan)"
    OFF)

set(ASN1KIT_BER_DER_FIXTURES_DIR
    "${CMAKE_CURRENT_SOURCE_DIR}/../runtime-csharp/fixtures/ber-der"
    CACHE PATH "Path to runtime-csharp ber-der JSON fixtures")

add_library(asn1kit STATIC
    src/tag.cpp
    src/encode_buffer.cpp
    src/decode_cursor.cpp
    src/constructed_decoder.cpp
    src/text_codec.cpp
    src/reader.cpp
    src/writer.cpp
    src/integer.cpp
    src/oid.cpp
    src/bit_string.cpp
    src/null.cpp
    src/boolean.cpp
    src/utc_date_time.cpp
)
add_library(asn1kit::asn1kit ALIAS asn1kit)

target_include_directories(asn1kit
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/src
)

if(MSVC)
    target_compile_options(asn1kit PRIVATE /W4 /permissive- /utf-8)
else()
    target_compile_options(asn1kit PRIVATE -Wall -Wextra -Wpedantic)
endif()

if(ASN1KIT_BUILD_TESTS)
    enable_testing()
    if(ASN1KIT_FETCH_TEST_DEPS)
        include(FetchContent)
        set(FETCHCONTENT_BASE_DIR "${CMAKE_BINARY_DIR}/_deps" CACHE PATH "" FORCE)
        set(BUILD_GMOCK OFF CACHE BOOL "" FORCE)
        set(INSTALL_GTEST OFF CACHE BOOL "" FORCE)
        FetchContent_Declare(
            googletest
            GIT_REPOSITORY https://github.com/google/googletest.git
            GIT_TAG v1.15.0
            GIT_SHALLOW TRUE
        )
        set(JSON_BuildTests OFF CACHE BOOL "" FORCE)
        FetchContent_Declare(
            nlohmann_json
            GIT_REPOSITORY https://github.com/nlohmann/json.git
            GIT_TAG v3.11.3
            GIT_SHALLOW TRUE
        )
        FetchContent_MakeAvailable(googletest nlohmann_json)
    else()
        find_package(GTest REQUIRED)
        find_package(nlohmann_json REQUIRED)
    endif()
    add_subdirectory(tests)
endif()
