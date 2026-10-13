// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#pragma once

#include "asn1kit/any.hpp"
#include "asn1kit/bit_string.hpp"
#include "asn1kit/bytes.hpp"
#include "asn1kit/contained.hpp"
#include "asn1kit/integer.hpp"
#include "asn1kit/lazy.hpp"
#include "asn1kit/null.hpp"
#include "asn1kit/oid.hpp"
#include "asn1kit/utc_date_time.hpp"
#include "asn1kit/value.hpp"

#include <charconv>
#include <cstddef>
#include <cstdint>
#include <optional>
#include <ostream>
#include <string>
#include <string_view>
#include <vector>

namespace asn1kit {

namespace detail {

/// Writes an integer without locale-dependent separators.
template <typename T>
void write_format_decimal(std::ostream& out, T value) {
    char buffer[32];
    const auto result = std::to_chars(buffer, buffer + sizeof(buffer), value);
    out.write(buffer, result.ptr - buffer);
}

/// Writes `indent` levels of the four-space debug prefix.
inline void write_format_indent(std::ostream& out, int indent) {
    for (int i = 0; i < indent; ++i) {
        out << "    ";
    }
}

} // namespace detail

// Primitive overloads. Generated type overloads live in their own namespace and
// are found through ADL; the container overloads below recurse into them.

inline void to_string(bool value, std::ostream& out, int /*indent*/) {
    out << (value ? "true" : "false");
}

inline void to_string(std::int32_t value, std::ostream& out, int /*indent*/) {
    detail::write_format_decimal(out, value);
}

inline void to_string(std::uint32_t value, std::ostream& out, int /*indent*/) {
    detail::write_format_decimal(out, value);
}

inline void to_string(std::int64_t value, std::ostream& out, int /*indent*/) {
    detail::write_format_decimal(out, value);
}

inline void to_string(std::uint64_t value, std::ostream& out, int /*indent*/) {
    detail::write_format_decimal(out, value);
}

inline void to_string(const std::string& value, std::ostream& out, int /*indent*/) {
    out << value;
}

inline void to_string(std::string_view value, std::ostream& out, int /*indent*/) {
    out << value;
}

inline void to_string(const integer& value, std::ostream& out, int /*indent*/) {
    out << value.to_decimal();
}

inline void to_string(const null_value& /*value*/, std::ostream& out, int /*indent*/) {
    out << "null";
}

inline void to_string(const oid& value, std::ostream& out, int /*indent*/) {
    out << value.to_string();
}

inline void to_string(const bytes& value, std::ostream& out, int /*indent*/) {
    detail::write_format_decimal(out, value.size());
    out << " bytes";
}

inline void to_string(const bit_string& value, std::ostream& out, int /*indent*/) {
    detail::write_format_decimal(out, value.bit_length());
    out << " bits";
}

inline void to_string(const utc_date_time& value, std::ostream& out, int /*indent*/) {
    out << value.to_iso();
}

inline void to_string(const any& value, std::ostream& out, int /*indent*/) {
    out << value.to_string();
}

template <typename T>
void to_string(const std::optional<T>& value, std::ostream& out, int indent) {
    if (value.has_value()) {
        to_string(*value, out, indent);
    } else {
        out << "<absent>";
    }
}

template <typename T>
void to_string(const std::vector<T>& value, std::ostream& out, int indent) {
    out << '[';
    detail::write_format_decimal(out, value.size());
    out << "] {";
    for (const auto& item : value) {
        out << '\n';
        detail::write_format_indent(out, indent + 1);
        to_string(item, out, indent + 1);
    }

    out << '\n';
    detail::write_format_indent(out, indent);
    out << '}';
}

template <typename T>
void to_string(const value<T>& value, std::ostream& out, int indent) {
    to_string(value.get(), out, indent);
}

template <typename T>
void to_string(const lazy<T>& value, std::ostream& out, int indent) {
    if (value.is_materialized()) {
        to_string(value.get(), out, indent);
    } else if (value.has_encoded()) {
        out << "<not decoded: ";
        detail::write_format_decimal(out, value.encoded().size());
        out << " bytes>";
    } else {
        out << "<unset>";
    }
}

template <typename T>
void to_string(const contained<T>& value, std::ostream& out, int indent) {
    if (value.has_value()) {
        to_string(value.value(), out, indent);
    } else {
        out << "<opaque: ";
        detail::write_format_decimal(out, value.contents().size());
        out << " bytes>";
    }
}

/// Public entry point: writes a multi-line, indented debug view of `value`.
/// No trailing newline is written, so callers control line framing.
template <typename T>
void to_string(const T& value, std::ostream& out) {
    to_string(value, out, 0);
}

} // namespace asn1kit
