// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "fixture_loader.hpp"

#include <fstream>
#include <stdexcept>
#include <string>

#ifndef ASN1KIT_PKIX_FIXTURES_DIR
#error ASN1KIT_PKIX_FIXTURES_DIR must be defined
#endif
#ifndef ASN1KIT_CMS_FIXTURES_DIR
#error ASN1KIT_CMS_FIXTURES_DIR must be defined
#endif

namespace asn1kit::benchmarks {
namespace {

[[nodiscard]] std::vector<std::uint8_t> read_file(const std::string& path) {
    std::ifstream input(path, std::ios::binary);
    if (!input) {
        throw std::runtime_error("Missing fixture: " + path);
    }
    return std::vector<std::uint8_t>(
        (std::istreambuf_iterator<char>(input)), std::istreambuf_iterator<char>());
}

} // namespace

std::vector<std::uint8_t> read_pkix_fixture(std::string_view name) {
    return read_file(std::string(ASN1KIT_PKIX_FIXTURES_DIR) + "/" + std::string(name));
}

std::vector<std::uint8_t> read_cms_fixture(std::string_view name) {
    return read_file(std::string(ASN1KIT_CMS_FIXTURES_DIR) + "/" + std::string(name));
}

} // namespace asn1kit::benchmarks
