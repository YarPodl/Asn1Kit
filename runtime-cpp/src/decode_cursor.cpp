// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "decode_cursor.hpp"

#include "asn1kit/exception.hpp"

#include <limits>

namespace asn1kit::detail {

decode_cursor::decode_cursor(bytes data, encoding enc, reader_options options)
    : data_(std::move(data)), encoding_(enc), options_(options) {}

bool decode_cursor::eof() const noexcept {
    return offset_ >= static_cast<int>(data_.size());
}

int decode_cursor::remaining() const noexcept {
    return static_cast<int>(data_.size()) - offset_;
}

decode_cursor decode_cursor::create_nested(bytes contents) const {
    return decode_cursor(std::move(contents), encoding_, options_);
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
    const int encoded_start = offset_;
    auto tag_value = read_tag();
    auto [length, indefinite] = read_length();
    bytes contents;

    if (indefinite) {
        if (encoding_ == encoding::der) {
            throw exception("Indefinite length is not allowed in DER.");
        }
        if (!tag_value.constructed()) {
            throw exception("Indefinite length requires a constructed tag.");
        }
        contents = read_indefinite_contents();
    } else {
        if (length > remaining()) {
            throw exception("Length exceeds buffer.");
        }
        contents = data_.slice(static_cast<std::size_t>(offset_), static_cast<std::size_t>(length));
        offset_ += length;
    }

    return tlv{
        tag_value,
        contents,
        data_.slice(
            static_cast<std::size_t>(encoded_start),
            static_cast<std::size_t>(offset_ - encoded_start)),
    };
}

bytes decode_cursor::read_indefinite_contents() {
    const int contents_start = offset_;
    while (true) {
        if (remaining() < 2) {
            throw exception("Unterminated indefinite length.");
        }
        const auto span = data_.span();
        if (span[static_cast<std::size_t>(offset_)] == 0x00
            && span[static_cast<std::size_t>(offset_ + 1)] == 0x00) {
            auto contents = data_.slice(
                static_cast<std::size_t>(contents_start),
                static_cast<std::size_t>(offset_ - contents_start));
            offset_ += 2;
            return contents;
        }
        (void)read_tlv();
    }
}

tag decode_cursor::read_tag() {
    ensure_available(1);
    const auto span = data_.span();
    const std::uint8_t first = span[static_cast<std::size_t>(offset_++)];
    const auto cls = static_cast<tag_class>((first & 0xC0) >> 6);
    const bool constructed = (first & 0x20) != 0;
    int number = first & 0x1F;

    if (number == 0x1F) {
        number = read_high_tag_number();
    }

    if (cls == tag_class::universal && number == 0) {
        throw exception("Unexpected end-of-contents tag.");
    }

    return tag(cls, number, constructed);
}

int decode_cursor::read_high_tag_number() {
    ensure_available(1);
    const auto span = data_.span();
    if (span[static_cast<std::size_t>(offset_)] == 0x80) {
        throw exception("High-tag-number form is not minimally encoded.");
    }

    int number = 0;
    std::uint8_t current = 0;
    do {
        ensure_available(1);
        current = span[static_cast<std::size_t>(offset_++)];
        const int payload = current & 0x7F;
        if (number > (std::numeric_limits<int>::max() - payload) / 128) {
            throw exception("Tag number exceeds Int32.");
        }
        number = (number * 128) + payload;
    } while ((current & 0x80) != 0);

    if (number < 31) {
        throw exception("High-tag-number form is not minimally encoded.");
    }
    return number;
}

std::pair<int, bool> decode_cursor::read_length() {
    ensure_available(1);
    const auto span = data_.span();
    const std::uint8_t first = span[static_cast<std::size_t>(offset_++)];
    if (first == 0x80) {
        return {0, true};
    }
    if ((first & 0x80) == 0) {
        return {first, false};
    }

    const int count = first & 0x7F;
    if (count == 0 || count > 4) {
        throw exception("Unsupported length form.");
    }
    ensure_available(count);
    if (options_.reject_non_minimal_length && span[static_cast<std::size_t>(offset_)] == 0x00) {
        throw exception("Non-minimal length encoding.");
    }

    std::uint32_t length = 0;
    for (int i = 0; i < count; ++i) {
        length = (length << 8) | span[static_cast<std::size_t>(offset_++)];
    }
    if (length > static_cast<std::uint32_t>(std::numeric_limits<int>::max())) {
        throw exception("Length exceeds Int32.");
    }
    if (options_.reject_non_minimal_length && length < 128) {
        throw exception("Non-minimal length encoding.");
    }
    return {static_cast<int>(length), false};
}

void decode_cursor::ensure_available(int count) const {
    if (count < 0 || count > remaining()) {
        throw exception("Unexpected end of ASN.1 data.");
    }
}

} // namespace asn1kit::detail
