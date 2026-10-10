// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "asn1kit/encoding.hpp"
#include "asn1kit/exception.hpp"

#include <cstdint>
#include <span>

namespace asn1kit {

bool decode_boolean_contents(std::span<const std::uint8_t> contents, encoding enc) {
    if (contents.size() != 1) {
        throw exception("BOOLEAN must contain one octet.");
    }
    if (enc == encoding::der && contents[0] != 0x00 && contents[0] != 0xFF) {
        throw exception("DER BOOLEAN must be 0x00 or 0xFF.");
    }
    return contents[0] != 0x00;
}

} // namespace asn1kit
