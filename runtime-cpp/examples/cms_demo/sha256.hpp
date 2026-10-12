// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#pragma once

#include <cstdint>
#include <span>
#include <vector>

namespace asn1kit::cms_demo {

/// Compact SHA-256 for educational CMS signing (messageDigest only).
[[nodiscard]] std::vector<std::uint8_t> sha256(std::span<const std::uint8_t> data);

} // namespace asn1kit::cms_demo
