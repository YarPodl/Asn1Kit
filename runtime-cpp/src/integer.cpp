// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "asn1kit/integer.hpp"

#include "asn1kit/reader.hpp"
#include "asn1kit/writer.hpp"

#include <algorithm>
#include <array>
#include <cstring>
#include <limits>

namespace asn1kit {
namespace {

const std::uint8_t k_zero_contents[1] = {0x00};

constexpr std::array<std::uint8_t, 256> make_single_octet_table() {
    std::array<std::uint8_t, 256> table{};
    for (std::size_t i = 0; i < table.size(); ++i) {
        table[i] = static_cast<std::uint8_t>(i);
    }
    return table;
}

constexpr auto k_single_octet_contents = make_single_octet_table();

integer from_single_octet(int value) {
    const auto b = static_cast<std::uint8_t>(value);
    return integer::from_contents(bytes::borrow(std::span<const std::uint8_t>(&k_single_octet_contents[b], 1)));
}

void ensure_encode_destination(std::span<std::uint8_t> destination, int required) {
    if (static_cast<int>(destination.size()) < required) {
        throw exception("INTEGER encode destination is too small.");
    }
}

int encode_signed_contents(std::int64_t value, std::span<std::uint8_t> destination) {
    std::uint8_t full[8];
    for (int i = 7; i >= 0; --i) {
        full[i] = static_cast<std::uint8_t>(value & 0xFF);
        value >>= 8;
    }

    int start = 0;
    // Restore value for sign check from first byte of full
    const bool negative = (full[0] & 0x80) != 0;
    if (!negative) {
        while (start < 7 && full[start] == 0x00 && (full[start + 1] & 0x80) == 0) {
            ++start;
        }
    } else {
        while (start < 7 && full[start] == 0xFF && (full[start + 1] & 0x80) != 0) {
            ++start;
        }
    }

    const int length = 8 - start;
    ensure_encode_destination(destination, length);
    std::memcpy(destination.data(), full + start, static_cast<std::size_t>(length));
    return length;
}

int encode_unsigned_contents(std::uint64_t value, std::span<std::uint8_t> destination) {
    if (value <= static_cast<std::uint64_t>(std::numeric_limits<std::int64_t>::max())) {
        return encode_signed_contents(static_cast<std::int64_t>(value), destination);
    }
    ensure_encode_destination(destination, 9);
    destination[0] = 0x00;
    for (int i = 7; i >= 0; --i) {
        destination[static_cast<std::size_t>(1 + i)] = static_cast<std::uint8_t>(value & 0xFF);
        value >>= 8;
    }
    return 9;
}

bool try_read_signed(std::span<const std::uint8_t> contents, int max_bytes, std::int64_t& value) {
    if (contents.empty()) {
        return false;
    }
    auto span = contents;
    while (span.size() > 1
           && ((span[0] == 0x00 && (span[1] & 0x80) == 0)
               || (span[0] == 0xFF && (span[1] & 0x80) != 0))) {
        span = span.subspan(1);
    }
    if (static_cast<int>(span.size()) > max_bytes) {
        return false;
    }
    std::int64_t result = static_cast<std::int8_t>(span[0]);
    for (std::size_t i = 1; i < span.size(); ++i) {
        result = (result << 8) | span[i];
    }
    value = result;
    return true;
}

bool try_read_unsigned(std::span<const std::uint8_t> contents, int max_bytes, std::uint64_t& value) {
    if (contents.empty() || (contents[0] & 0x80) != 0) {
        return false;
    }
    auto span = contents;
    while (span.size() > 1 && span[0] == 0x00 && (span[1] & 0x80) == 0) {
        span = span.subspan(1);
    }
    if (span.size() > 1 && span[0] == 0x00) {
        span = span.subspan(1);
    }
    if (static_cast<int>(span.size()) > max_bytes) {
        return false;
    }
    std::uint64_t result = 0;
    for (auto b : span) {
        result = (result << 8) | b;
    }
    value = result;
    return true;
}

void mul_add_u8(std::vector<std::uint8_t>& mag, int digit) {
    // mag is big-endian magnitude
    int carry = digit;
    for (int i = static_cast<int>(mag.size()) - 1; i >= 0; --i) {
        const int prod = static_cast<int>(mag[static_cast<std::size_t>(i)]) * 10 + carry;
        mag[static_cast<std::size_t>(i)] = static_cast<std::uint8_t>(prod & 0xFF);
        carry = prod >> 8;
    }
    while (carry > 0) {
        mag.insert(mag.begin(), static_cast<std::uint8_t>(carry & 0xFF));
        carry >>= 8;
    }
}

std::vector<std::uint8_t> decimal_to_twos_complement(std::string_view text) {
    if (text.empty()) {
        throw exception("INTEGER decimal is empty.");
    }
    bool negative = false;
    std::size_t i = 0;
    if (text[0] == '-') {
        negative = true;
        ++i;
    } else if (text[0] == '+') {
        ++i;
    }
    if (i >= text.size()) {
        throw exception("INTEGER decimal is empty.");
    }
    while (i < text.size() && text[i] == '0') {
        ++i;
    }
    if (i >= text.size()) {
        return {0x00};
    }

    std::vector<std::uint8_t> mag;
    for (; i < text.size(); ++i) {
        if (text[i] < '0' || text[i] > '9') {
            throw exception("INTEGER decimal has invalid characters.");
        }
        mul_add_u8(mag, text[i] - '0');
    }
    if (mag.empty()) {
        return {0x00};
    }

    if (!negative) {
        if ((mag[0] & 0x80) != 0) {
            mag.insert(mag.begin(), 0x00);
        }
        return mag;
    }

    // Two's complement of magnitude
    for (auto& b : mag) {
        b = static_cast<std::uint8_t>(~b);
    }
    for (int j = static_cast<int>(mag.size()) - 1; j >= 0; --j) {
        const int sum = mag[static_cast<std::size_t>(j)] + 1;
        mag[static_cast<std::size_t>(j)] = static_cast<std::uint8_t>(sum & 0xFF);
        if (sum <= 0xFF) {
            break;
        }
    }
    // Ensure negative sign bit
    if ((mag[0] & 0x80) == 0) {
        mag.insert(mag.begin(), 0xFF);
    }
    // Strip redundant leading 0xFF
    while (mag.size() > 1 && mag[0] == 0xFF && (mag[1] & 0x80) != 0) {
        mag.erase(mag.begin());
    }
    return mag;
}

std::string twos_complement_to_decimal(std::span<const std::uint8_t> contents) {
    if (contents.empty()) {
        throw exception("INTEGER contents must not be empty.");
    }
    const bool negative = (contents[0] & 0x80) != 0;
    std::vector<std::uint8_t> mag(contents.begin(), contents.end());
    if (negative) {
        for (auto& b : mag) {
            b = static_cast<std::uint8_t>(~b);
        }
        for (int j = static_cast<int>(mag.size()) - 1; j >= 0; --j) {
            const int sum = mag[static_cast<std::size_t>(j)] + 1;
            mag[static_cast<std::size_t>(j)] = static_cast<std::uint8_t>(sum & 0xFF);
            if (sum <= 0xFF) {
                break;
            }
        }
    }

    while (mag.size() > 1 && mag[0] == 0) {
        mag.erase(mag.begin());
    }

    if (mag.size() == 1 && mag[0] == 0) {
        return "0";
    }

    std::string digits;
    std::vector<std::uint8_t> work = mag;
    while (!(work.size() == 1 && work[0] == 0)) {
        int rem = 0;
        for (std::size_t i = 0; i < work.size(); ++i) {
            const int cur = rem * 256 + work[i];
            work[i] = static_cast<std::uint8_t>(cur / 10);
            rem = cur % 10;
        }
        digits.push_back(static_cast<char>('0' + rem));
        std::size_t start = 0;
        while (start + 1 < work.size() && work[start] == 0) {
            ++start;
        }
        if (start > 0) {
            work.erase(work.begin(), work.begin() + static_cast<std::ptrdiff_t>(start));
        }
    }
    std::reverse(digits.begin(), digits.end());
    if (negative) {
        return "-" + digits;
    }
    return digits;
}

} // namespace

std::span<const std::uint8_t> integer::span() const noexcept {
    if (bytes_.empty()) {
        return std::span<const std::uint8_t>(k_zero_contents, 1);
    }
    return bytes_.span();
}

integer integer::from_contents(bytes contents) {
    if (contents.empty()) {
        throw exception("INTEGER contents must not be empty.");
    }
    return integer(std::move(contents));
}

integer integer::copy_from(std::span<const std::uint8_t> contents) {
    if (contents.empty()) {
        throw exception("INTEGER contents must not be empty.");
    }
    return integer(bytes::copy_from(contents));
}

integer integer::from_int32(std::int32_t value) {
    if (value == 0) {
        return {};
    }
    if (value >= -128 && value <= 127) {
        return from_single_octet(value);
    }
    std::uint8_t buf[4];
    const int n = encode_signed_contents(value, buf);
    return copy_from(std::span<const std::uint8_t>(buf, static_cast<std::size_t>(n)));
}

integer integer::from_uint32(std::uint32_t value) {
    if (value == 0) {
        return {};
    }
    if (value <= 127) {
        return from_single_octet(static_cast<int>(value));
    }
    std::uint8_t buf[5];
    const int n = encode_unsigned_contents(value, buf);
    return copy_from(std::span<const std::uint8_t>(buf, static_cast<std::size_t>(n)));
}

integer integer::from_int64(std::int64_t value) {
    if (value == 0) {
        return {};
    }
    if (value >= -128 && value <= 127) {
        return from_single_octet(static_cast<int>(value));
    }
    std::uint8_t buf[8];
    const int n = encode_signed_contents(value, buf);
    return copy_from(std::span<const std::uint8_t>(buf, static_cast<std::size_t>(n)));
}

integer integer::from_uint64(std::uint64_t value) {
    if (value == 0) {
        return {};
    }
    if (value <= 127) {
        return from_single_octet(static_cast<int>(value));
    }
    std::uint8_t buf[9];
    const int n = encode_unsigned_contents(value, buf);
    return copy_from(std::span<const std::uint8_t>(buf, static_cast<std::size_t>(n)));
}

integer integer::from_decimal(std::string_view text) {
    auto contents = decimal_to_twos_complement(text);
    return copy_from(contents);
}

std::string integer::to_decimal() const {
    return twos_complement_to_decimal(span());
}

bool integer::is_minimal_contents(std::span<const std::uint8_t> contents) {
    if (contents.size() <= 1) {
        return true;
    }
    if (contents[0] == 0x00 && (contents[1] & 0x80) == 0) {
        return false;
    }
    if (contents[0] == 0xFF && (contents[1] & 0x80) != 0) {
        return false;
    }
    return true;
}

int integer::encode_contents(std::int32_t value, std::span<std::uint8_t> destination) {
    return encode_signed_contents(value, destination);
}

int integer::encode_contents(std::uint32_t value, std::span<std::uint8_t> destination) {
    return encode_unsigned_contents(value, destination);
}

int integer::encode_contents(std::int64_t value, std::span<std::uint8_t> destination) {
    return encode_signed_contents(value, destination);
}

int integer::encode_contents(std::uint64_t value, std::span<std::uint8_t> destination) {
    return encode_unsigned_contents(value, destination);
}

bool integer::try_get_int32(std::int32_t& value) const {
    std::int64_t signed_value = 0;
    if (!try_read_signed(span(), 4, signed_value)
        || signed_value < std::numeric_limits<std::int32_t>::min()
        || signed_value > std::numeric_limits<std::int32_t>::max()) {
        return false;
    }
    value = static_cast<std::int32_t>(signed_value);
    return true;
}

bool integer::try_get_uint32(std::uint32_t& value) const {
    std::uint64_t unsigned_value = 0;
    if (!try_read_unsigned(span(), 4, unsigned_value)
        || unsigned_value > std::numeric_limits<std::uint32_t>::max()) {
        return false;
    }
    value = static_cast<std::uint32_t>(unsigned_value);
    return true;
}

bool integer::try_get_int64(std::int64_t& value) const {
    return try_read_signed(span(), 8, value);
}

bool integer::try_get_uint64(std::uint64_t& value) const {
    return try_read_unsigned(span(), 8, value);
}

std::int32_t integer::get_int32() const {
    std::int32_t value = 0;
    if (!try_get_int32(value)) {
        throw exception("INTEGER value does not fit in Int32.");
    }
    return value;
}

std::uint32_t integer::get_uint32() const {
    std::uint32_t value = 0;
    if (!try_get_uint32(value)) {
        throw exception("INTEGER value does not fit in UInt32.");
    }
    return value;
}

std::int64_t integer::get_int64() const {
    std::int64_t value = 0;
    if (!try_get_int64(value)) {
        throw exception("INTEGER value does not fit in Int64.");
    }
    return value;
}

std::uint64_t integer::get_uint64() const {
    std::uint64_t value = 0;
    if (!try_get_uint64(value)) {
        throw exception("INTEGER value does not fit in UInt64.");
    }
    return value;
}

void integer::encode(writer& w, const integer& value, const tag& t) {
    w.write_integer(t, value);
}

void integer::encode(writer& w, std::int32_t value, const tag& t) {
    w.write_integer(t, value);
}

integer integer::decode(reader& r, const tag& t) {
    return r.read_integer_value(t);
}

bool operator==(const integer& a, const integer& b) {
    const auto left = a.span();
    const auto right = b.span();
    if (left.size() != right.size()) {
        return false;
    }
    return std::memcmp(left.data(), right.data(), left.size()) == 0;
}

} // namespace asn1kit
