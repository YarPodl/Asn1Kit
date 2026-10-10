// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#pragma once

namespace asn1kit {

/// Decode-time strictness knobs. Encode stays canonical regardless of these flags.
struct reader_options {
    /// Soft profile: non-minimal INTEGER and nonzero BIT STRING trailing bits are accepted;
    /// non-minimal length and overlong OID base-128 are rejected.
    static reader_options default_profile() { return {}; }

    /// All optional rejects enabled.
    static reader_options strict() {
        reader_options o;
        o.reject_non_minimal_integer = true;
        o.reject_bit_string_trailing_bits = true;
        o.reject_non_minimal_length = true;
        o.reject_overlong_oid_base128 = true;
        return o;
    }

    /// Softened length checks only.
    static reader_options allow_non_minimal_length() {
        reader_options o;
        o.reject_non_minimal_length = false;
        return o;
    }

    /// Softened OID base-128 checks only.
    static reader_options allow_overlong_oid_base128() {
        reader_options o;
        o.reject_overlong_oid_base128 = false;
        return o;
    }

    bool reject_non_minimal_integer{false};
    bool reject_bit_string_trailing_bits{false};
    bool reject_non_minimal_length{true};
    bool reject_overlong_oid_base128{true};
};

} // namespace asn1kit
