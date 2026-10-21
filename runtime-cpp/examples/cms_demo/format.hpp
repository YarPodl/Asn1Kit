// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#pragma once

#include "PKIX1Implicit2009.hpp"
#include "PKIXCommonTypes2009.hpp"

#include "asn1kit/bytes.hpp"
#include "asn1kit/oid.hpp"
#include "asn1kit/utc_date_time.hpp"

#include <cstdint>
#include <ostream>
#include <span>
#include <string>
#include <vector>

namespace asn1kit::cms_demo {

[[nodiscard]] std::string hex_bytes(std::span<const std::uint8_t> data, std::size_t max = 16);

[[nodiscard]] std::string format_oid(const asn1kit::oid& value);

[[nodiscard]] std::string format_time(const asn1kit::utc_date_time& value);

[[nodiscard]] std::string format_key_usage(
    asn1kit::modern::PKIX1Implicit2009::KeyUsageFlags flags);

[[nodiscard]] std::string format_attribute(
    const asn1kit::modern::PKIXCommonTypes2009::SingleAttribute& attr);

[[nodiscard]] std::string format_name(
    const std::vector<std::vector<asn1kit::modern::PKIXCommonTypes2009::SingleAttribute>>& name);

[[nodiscard]] bool has_key_usage_flag(
    asn1kit::modern::PKIX1Implicit2009::KeyUsageFlags flags,
    asn1kit::modern::PKIX1Implicit2009::KeyUsageFlags bit);

void write_indent(std::ostream& out, int depth);

} // namespace asn1kit::cms_demo
