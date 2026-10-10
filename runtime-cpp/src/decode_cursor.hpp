// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#pragma once

#include "asn1kit/bytes.hpp"
#include "asn1kit/encoding.hpp"
#include "asn1kit/reader_options.hpp"
#include "asn1kit/tag.hpp"

#include <cstdint>
#include <utility>

namespace asn1kit::detail {

struct tlv {
    tag tag_value{};
    bytes contents{};
    bytes encoded{};
};

/// Allocation-free TLV cursor over one decode window.
class decode_cursor {
public:
    decode_cursor(bytes data, encoding enc, reader_options options);

    [[nodiscard]] encoding encoding_rules() const noexcept { return encoding_; }
    [[nodiscard]] const reader_options& options() const noexcept { return options_; }
    [[nodiscard]] bool eof() const noexcept;
    [[nodiscard]] int remaining() const noexcept;
    [[nodiscard]] const bytes& data() const noexcept { return data_; }
    [[nodiscard]] int offset() const noexcept { return offset_; }

    [[nodiscard]] decode_cursor create_nested(bytes contents) const;

    [[nodiscard]] bool try_peek_tag(tag& out) const;
    [[nodiscard]] tlv read_tlv();

private:
    [[nodiscard]] bytes read_indefinite_contents();
    [[nodiscard]] tag read_tag();
    [[nodiscard]] int read_high_tag_number();
    [[nodiscard]] std::pair<int, bool> read_length();
    void ensure_available(int count) const;

    bytes data_;
    encoding encoding_;
    reader_options options_;
    int offset_{0};
};

} // namespace asn1kit::detail
