// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#pragma once

#include "asn1kit/tag.hpp"

namespace asn1kit {

class writer;
class reader;

/// ASN.1 NULL value (universal tag 5, empty contents).
struct null_value {
    [[nodiscard]] static null_value value() { return {}; }

    static void encode(writer& w, const tag& t = tag::null);
    static null_value decode(reader& r, const tag& t = tag::null);

    friend bool operator==(const null_value&, const null_value&) noexcept { return true; }
    friend bool operator!=(const null_value&, const null_value&) noexcept { return false; }
};

} // namespace asn1kit
