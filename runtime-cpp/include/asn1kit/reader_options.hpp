// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#pragma once

#include <cstddef>

namespace asn1kit {

/// Decode-time strictness knobs. Encode stays canonical regardless of these flags.
struct reader_options {
    static constexpr std::size_t default_max_recursion_depth = 64;
    static constexpr std::size_t default_max_materialized_constructed_value_bytes = 100 * 1024 * 1024;
    static constexpr std::size_t default_max_constructed_segments = 4096;

    /// Soft profile: non-minimal INTEGER and nonzero BIT STRING trailing bits are accepted;
    /// non-minimal length and overlong OID base-128 are rejected.
    [[nodiscard]] static constexpr reader_options default_profile() { return {}; }

    /// All optional rejects enabled.
    [[nodiscard]] static constexpr reader_options strict() {
        reader_options o;
        o.reject_non_minimal_integer = true;
        o.reject_bit_string_trailing_bits = true;
        o.reject_non_minimal_length = true;
        o.reject_overlong_oid_base128 = true;
        return o;
    }

    /// Softened length checks only.
    [[nodiscard]] static constexpr reader_options allow_non_minimal_length() {
        reader_options o;
        o.reject_non_minimal_length = false;
        return o;
    }

    /// Softened OID base-128 checks only.
    [[nodiscard]] static constexpr reader_options allow_overlong_oid_base128() {
        reader_options o;
        o.reject_overlong_oid_base128 = false;
        return o;
    }

    bool reject_non_minimal_integer{false};
    bool reject_bit_string_trailing_bits{false};
    bool reject_non_minimal_length{true};
    bool reject_overlong_oid_base128{true};
    /// Bounds indefinite EOC scans, definite SEQUENCE/SET/EXPLICIT/OF nesting, and nested
    /// constructed string materialization.
    std::size_t max_recursion_depth{default_max_recursion_depth};
    std::size_t max_materialized_constructed_value_bytes{
        default_max_materialized_constructed_value_bytes};
    /// Maximum leaf primitive segments concatenated for one constructed OCTET/BIT/string/time.
    /// Zero permits only empty constructed payloads (no leaf segments).
    std::size_t max_constructed_segments{default_max_constructed_segments};
};

} // namespace asn1kit
