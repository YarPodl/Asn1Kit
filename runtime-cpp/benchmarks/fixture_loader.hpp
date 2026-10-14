// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#pragma once

#include <cstdint>
#include <string>
#include <string_view>
#include <vector>

namespace asn1kit::benchmarks {

[[nodiscard]] std::vector<std::uint8_t> read_pkix_fixture(std::string_view name);
[[nodiscard]] std::vector<std::uint8_t> read_cms_fixture(std::string_view name);

} // namespace asn1kit::benchmarks
