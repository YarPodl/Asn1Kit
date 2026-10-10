// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#pragma once

#include "asn1kit/bytes.hpp"
#include "asn1kit/exception.hpp"
#include "asn1kit/tag.hpp"

#include <cstddef>
#include <cstdint>
#include <span>
#include <string>
#include <string_view>
#include <vector>

namespace asn1kit {

class writer;
class reader;

/// OBJECT IDENTIFIER value: DER contents octets (no tag/length).
class oid {
public:
    oid() = default;

    [[nodiscard]] static oid from_contents(bytes contents);
    [[nodiscard]] static oid copy_from(std::span<const std::uint8_t> contents);
    [[nodiscard]] static oid parse(std::string_view dotted);

    [[nodiscard]] std::span<const std::uint8_t> span() const noexcept { return contents_.span(); }
    [[nodiscard]] const bytes& contents() const noexcept { return contents_; }
    [[nodiscard]] std::vector<std::uint8_t> to_vector() const { return contents_.to_vector(); }
    [[nodiscard]] oid clone() const { return copy_from(span()); }

    [[nodiscard]] std::string to_string() const;

    [[nodiscard]] static std::size_t get_encode_contents_max_length(std::string_view dotted);
    [[nodiscard]] static std::size_t encode_contents(std::string_view dotted, std::span<std::uint8_t> destination);
    [[nodiscard]] static std::vector<std::uint8_t> encode_contents(std::string_view dotted);

    [[nodiscard]] static int read_arc(
        std::span<const std::uint8_t> contents,
        std::size_t& offset,
        bool reject_overlong);

    static void encode(writer& w, const oid& value, const tag& t = tag::object_identifier);
    static void encode(writer& w, std::string_view dotted, const tag& t = tag::object_identifier);
    static oid decode(reader& r, const tag& t = tag::object_identifier);
    static std::string decode_string(reader& r, const tag& t = tag::object_identifier);

    friend bool operator==(const oid& a, const oid& b);
    friend bool operator!=(const oid& a, const oid& b) { return !(a == b); }

private:
    explicit oid(bytes contents) : contents_(std::move(contents)) {}

    bytes contents_{};
};

} // namespace asn1kit
