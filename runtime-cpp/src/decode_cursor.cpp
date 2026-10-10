// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "asn1kit/detail/decode_cursor.hpp"

#include <limits>

namespace asn1kit::detail {

decode_cursor::decode_cursor(bytes data, encoding enc, reader_options options)
    : decode_cursor(std::move(data), enc, std::move(options), 0) {}

decode_cursor::decode_cursor(bytes data, encoding enc, reader_options options, std::size_t base_offset)
    : data_(std::move(data)), encoding_(enc), options_(options), base_offset_(base_offset) {}

bool decode_cursor::eof() const noexcept {
    return offset_ >= data_.size();
}

std::size_t decode_cursor::remaining() const noexcept {
    return data_.size() - offset_;
}

std::size_t decode_cursor::absolute_offset_of(const bytes& window) const noexcept {
    const auto parent = data_.span();
    const auto child = window.span();
    if (parent.data() == nullptr) {
        return base_offset_;
    }
    if (child.data() == nullptr) {
        // Empty window with null data(): treat as start of parent when empty, else base.
        return base_offset_ + (parent.empty() ? 0 : parent.size());
    }
    if (child.data() >= parent.data() && child.data() <= parent.data() + parent.size()) {
        return base_offset_ + static_cast<std::size_t>(child.data() - parent.data());
    }
    return absolute_offset();
}

decode_cursor decode_cursor::create_nested(bytes contents) const {
    const std::size_t nested_base = absolute_offset_of(contents);
    return decode_cursor(std::move(contents), encoding_, options_, nested_base);
}

bool decode_cursor::try_peek_tag(tag& out) const {
    if (eof()) {
        out = tag{};
        return false;
    }
    auto copy = *this;
    out = copy.read_tag();
    return true;
}

tlv decode_cursor::read_tlv() {
    const std::size_t encoded_start = offset_;
    auto tag_value = read_tag();
    auto [length, indefinite] = read_length();
    bytes contents;

    if (indefinite) {
        if (encoding_ == encoding::der) {
            throw exception("Indefinite length is not allowed in DER.", absolute_offset());
        }
        if (!tag_value.constructed()) {
            throw exception("Indefinite length requires a constructed tag.", absolute_offset());
        }
        contents = read_indefinite_contents();
    } else {
        if (length > remaining()) {
            throw exception("Length exceeds buffer.", absolute_offset());
        }
        contents = data_.slice(offset_, length);
        offset_ += length;
    }

    return tlv{
        tag_value,
        contents,
        data_.slice(encoded_start, offset_ - encoded_start),
    };
}

bytes decode_cursor::read_indefinite_contents() {
    const std::size_t contents_start = offset_;
    while (true) {
        if (remaining() < 2) {
            throw exception("Unterminated indefinite length.", absolute_offset());
        }
        const auto span = data_.span();
        if (span[offset_] == 0x00 && span[offset_ + 1] == 0x00) {
            auto contents = data_.slice(contents_start, offset_ - contents_start);
            offset_ += 2;
            return contents;
        }
        (void)read_tlv();
    }
}

tag decode_cursor::read_tag() {
    ensure_available(1);
    const auto span = data_.span();
    const std::uint8_t first = span[offset_++];
    const auto cls = static_cast<tag_class>((first & 0xC0) >> 6);
    const bool constructed = (first & 0x20) != 0;
    int number = first & 0x1F;

    if (number == 0x1F) {
        number = read_high_tag_number();
    }

    if (cls == tag_class::universal && number == 0) {
        throw exception("Unexpected end-of-contents tag.", absolute_offset());
    }

    return tag(cls, number, constructed);
}

int decode_cursor::read_high_tag_number() {
    ensure_available(1);
    const auto span = data_.span();
    if (span[offset_] == 0x80) {
        throw exception("High-tag-number form is not minimally encoded.", absolute_offset());
    }

    int number = 0;
    std::uint8_t current = 0;
    do {
        ensure_available(1);
        current = span[offset_++];
        const int payload = current & 0x7F;
        if (number > (std::numeric_limits<int>::max() - payload) / 128) {
            throw exception("Tag number is too large.", absolute_offset());
        }
        number = (number * 128) + payload;
    } while ((current & 0x80) != 0);

    if (number < 31) {
        throw exception("High-tag-number form is not minimally encoded.", absolute_offset());
    }
    return number;
}

std::pair<std::size_t, bool> decode_cursor::read_length() {
    ensure_available(1);
    const auto span = data_.span();
    const std::uint8_t first = span[offset_++];
    if (first == 0x80) {
        return {0, true};
    }
    if ((first & 0x80) == 0) {
        return {first, false};
    }

    const std::size_t count = first & 0x7F;
    if (count == 0 || count > 4) {
        throw exception("Unsupported length form.", absolute_offset());
    }
    ensure_available(count);
    if (options_.reject_non_minimal_length && span[offset_] == 0x00) {
        throw exception("Non-minimal length encoding.", absolute_offset());
    }

    std::size_t length = 0;
    for (std::size_t i = 0; i < count; ++i) {
        length = (length << 8) | span[offset_++];
    }
    if (options_.reject_non_minimal_length && length < 128) {
        throw exception("Non-minimal length encoding.", absolute_offset());
    }
    return {length, false};
}

void decode_cursor::ensure_available(std::size_t count) const {
    if (count > remaining()) {
        throw exception("Unexpected end of ASN.1 data.", absolute_offset());
    }
}

} // namespace asn1kit::detail
