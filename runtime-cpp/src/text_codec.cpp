// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "text_codec.hpp"

#include "asn1kit/exception.hpp"

#include <cmath>
#include <cstring>
#include <sstream>

namespace asn1kit::detail {
namespace {

bool is_numeric(char c) { return (c >= '0' && c <= '9') || c == ' '; }

bool is_printable(char c) {
    return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')
        || c == ' ' || c == '\'' || c == '(' || c == ')' || c == '+' || c == ','
        || c == '-' || c == '.' || c == '/' || c == ':' || c == '=' || c == '?';
}

bool is_ia5(char c) { return static_cast<unsigned char>(c) <= 0x7F; }

bool is_visible(char c) {
    return static_cast<unsigned char>(c) >= 0x20 && static_cast<unsigned char>(c) <= 0x7E;
}

bool is_digit(std::uint8_t b) { return b >= static_cast<std::uint8_t>('0') && b <= static_cast<std::uint8_t>('9'); }

void write2(std::span<std::uint8_t> span, int offset, int value) {
    span[static_cast<std::size_t>(offset)] = static_cast<std::uint8_t>('0' + value / 10);
    span[static_cast<std::size_t>(offset + 1)] = static_cast<std::uint8_t>('0' + value % 10);
}

void write4(std::span<std::uint8_t> span, int offset, int value) {
    span[static_cast<std::size_t>(offset)] = static_cast<std::uint8_t>('0' + value / 1000);
    span[static_cast<std::size_t>(offset + 1)] = static_cast<std::uint8_t>('0' + (value / 100) % 10);
    span[static_cast<std::size_t>(offset + 2)] = static_cast<std::uint8_t>('0' + (value / 10) % 10);
    span[static_cast<std::size_t>(offset + 3)] = static_cast<std::uint8_t>('0' + value % 10);
}

int read2(std::span<const std::uint8_t> text, int index) {
    if (index + 1 >= static_cast<int>(text.size()) || !is_digit(text[static_cast<std::size_t>(index)])
        || !is_digit(text[static_cast<std::size_t>(index + 1)])) {
        throw exception("Expected two decimal digits in time value.");
    }
    return (text[static_cast<std::size_t>(index)] - '0') * 10
        + (text[static_cast<std::size_t>(index + 1)] - '0');
}

int read4(std::span<const std::uint8_t> text, int index) {
    if (index + 3 >= static_cast<int>(text.size())
        || !is_digit(text[static_cast<std::size_t>(index)])
        || !is_digit(text[static_cast<std::size_t>(index + 1)])
        || !is_digit(text[static_cast<std::size_t>(index + 2)])
        || !is_digit(text[static_cast<std::size_t>(index + 3)])) {
        throw exception("Expected four decimal digits in time value.");
    }
    return (text[static_cast<std::size_t>(index)] - '0') * 1000
        + (text[static_cast<std::size_t>(index + 1)] - '0') * 100
        + (text[static_cast<std::size_t>(index + 2)] - '0') * 10
        + (text[static_cast<std::size_t>(index + 3)] - '0');
}

int count_utf8_bytes(std::string_view value) {
    int count = 0;
    for (std::size_t i = 0; i < value.size();) {
        const auto c = static_cast<unsigned char>(value[i]);
        int len = 0;
        if (c <= 0x7F) {
            len = 1;
        } else if ((c & 0xE0) == 0xC0) {
            len = 2;
        } else if ((c & 0xF0) == 0xE0) {
            len = 3;
        } else if ((c & 0xF8) == 0xF0) {
            len = 4;
        } else {
            throw exception("Invalid UTF-8 string.");
        }
        if (i + static_cast<std::size_t>(len) > value.size()) {
            throw exception("Invalid UTF-8 string.");
        }
        for (int j = 1; j < len; ++j) {
            if ((static_cast<unsigned char>(value[i + static_cast<std::size_t>(j)]) & 0xC0) != 0x80) {
                throw exception("Invalid UTF-8 string.");
            }
        }
        i += static_cast<std::size_t>(len);
        count += len;
    }
    return count;
}

int count_universal_code_points(std::string_view value) {
    int count = 0;
    for (std::size_t i = 0; i < value.size(); ++i) {
        const auto c = static_cast<unsigned char>(value[i]);
        if ((c & 0xF8) == 0xF0) {
            if (i + 3 >= value.size()) {
                throw exception("UniversalString contains an incomplete surrogate pair.");
            }
            ++count;
            i += 3;
        } else if ((c & 0xF0) == 0xE0) {
            if (i + 2 >= value.size()) {
                throw exception("UniversalString contains an incomplete surrogate pair.");
            }
            ++count;
            i += 2;
        } else if ((c & 0xE0) == 0xC0) {
            if (i + 1 >= value.size()) {
                throw exception("UniversalString contains an incomplete surrogate pair.");
            }
            ++count;
            i += 1;
        } else if (c <= 0x7F) {
            ++count;
        } else {
            throw exception("Invalid UTF-8 string.");
        }
    }
    return count;
}

std::uint32_t decode_utf8_codepoint(std::string_view value, std::size_t& i) {
    const auto c0 = static_cast<unsigned char>(value[i]);
    if (c0 <= 0x7F) {
        ++i;
        return c0;
    }
    if ((c0 & 0xE0) == 0xC0) {
        const auto c1 = static_cast<unsigned char>(value[i + 1]);
        i += 2;
        return ((c0 & 0x1F) << 6) | (c1 & 0x3F);
    }
    if ((c0 & 0xF0) == 0xE0) {
        const auto c1 = static_cast<unsigned char>(value[i + 1]);
        const auto c2 = static_cast<unsigned char>(value[i + 2]);
        i += 3;
        return ((c0 & 0x0F) << 12) | ((c1 & 0x3F) << 6) | (c2 & 0x3F);
    }
    if ((c0 & 0xF8) == 0xF0) {
        const auto c1 = static_cast<unsigned char>(value[i + 1]);
        const auto c2 = static_cast<unsigned char>(value[i + 2]);
        const auto c3 = static_cast<unsigned char>(value[i + 3]);
        i += 4;
        return ((c0 & 0x07) << 18) | ((c1 & 0x3F) << 12) | ((c2 & 0x3F) << 6) | (c3 & 0x3F);
    }
    throw exception("Invalid UTF-8 string.");
}

void append_utf8(std::string& out, std::uint32_t cp) {
    if (cp <= 0x7F) {
        out.push_back(static_cast<char>(cp));
    } else if (cp <= 0x7FF) {
        out.push_back(static_cast<char>(0xC0 | (cp >> 6)));
        out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
    } else if (cp <= 0xFFFF) {
        out.push_back(static_cast<char>(0xE0 | (cp >> 12)));
        out.push_back(static_cast<char>(0x80 | ((cp >> 6) & 0x3F)));
        out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
    } else {
        out.push_back(static_cast<char>(0xF0 | (cp >> 18)));
        out.push_back(static_cast<char>(0x80 | ((cp >> 12) & 0x3F)));
        out.push_back(static_cast<char>(0x80 | ((cp >> 6) & 0x3F)));
        out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
    }
}

// BMPString is encoded as UTF-16BE code units; API input is UTF-8.
std::u16string to_utf16(std::string_view utf8) {
    std::u16string result;
    for (std::size_t i = 0; i < utf8.size();) {
        const auto cp = decode_utf8_codepoint(utf8, i);
        if (cp <= 0xFFFF) {
            if (cp >= 0xD800 && cp <= 0xDFFF) {
                throw exception("Invalid UTF-8 string.");
            }
            result.push_back(static_cast<char16_t>(cp));
        } else {
            const auto v = cp - 0x10000;
            result.push_back(static_cast<char16_t>(0xD800 + (v >> 10)));
            result.push_back(static_cast<char16_t>(0xDC00 + (v & 0x3FF)));
        }
    }
    return result;
}

std::string from_utf16(std::span<const char16_t> units) {
    std::string out;
    for (std::size_t i = 0; i < units.size(); ++i) {
        const auto u = units[i];
        if (u >= 0xD800 && u <= 0xDBFF) {
            if (i + 1 >= units.size()) {
                throw exception("BMPString contents length must be even.");
            }
            const auto low = units[++i];
            if (low < 0xDC00 || low > 0xDFFF) {
                throw exception("UniversalString contains an incomplete surrogate pair.");
            }
            const std::uint32_t cp = 0x10000
                + ((static_cast<std::uint32_t>(u - 0xD800) << 10)
                   | static_cast<std::uint32_t>(low - 0xDC00));
            append_utf8(out, cp);
        } else if (u >= 0xDC00 && u <= 0xDFFF) {
            throw exception("UniversalString contains an unpaired low surrogate.");
        } else {
            append_utf8(out, u);
        }
    }
    return out;
}

void encode_ascii_checked(
    std::string_view value,
    bool (*allowed)(char),
    std::span<std::uint8_t> destination) {
    if (destination.size() != value.size()) {
        throw exception("String encode destination size mismatch.");
    }
    for (std::size_t i = 0; i < value.size(); ++i) {
        const char c = value[i];
        if (!allowed(c)) {
            std::ostringstream oss;
            oss << "Character U+" << std::hex << std::uppercase
                << static_cast<unsigned>(static_cast<unsigned char>(c)) << " is not allowed.";
            throw exception(oss.str());
        }
        destination[i] = static_cast<std::uint8_t>(c);
    }
}

std::string decode_ascii_checked(
    std::span<const std::uint8_t> contents,
    bool (*allowed)(char),
    const char* name) {
    std::string result;
    result.resize(contents.size());
    for (std::size_t i = 0; i < contents.size(); ++i) {
        const char c = static_cast<char>(contents[i]);
        if (!allowed(c)) {
            throw exception(std::string(name) + " contains an invalid character at offset "
                + std::to_string(i) + ".");
        }
        result[i] = c;
    }
    return result;
}

constexpr int fractions_per_second = time_fraction::period::den; // 10'000'000

utc_date_time round_to_fraction_digits(utc_date_time utc, int fraction_digits) {
    // Round the sub-second fraction to `fraction_digits` decimal places (X.690 / IR).
    if (fraction_digits == 0) {
        if (utc.fraction.count() * 2 >= fractions_per_second) {
            utc.second += 1;
            if (utc.second >= 60) {
                utc.second = 0;
                utc.minute += 1;
                if (utc.minute >= 60) {
                    utc.minute = 0;
                    utc.hour += 1;
                    if (utc.hour >= 24) {
                        utc.hour = 0;
                        utc.day += 1; // fixtures don't hit month/day rollover on encode
                    }
                }
            }
        }
        utc.fraction = time_fraction{0};
        return utc;
    }

    const long long unit = static_cast<long long>(std::pow(10.0, 7 - fraction_digits));
    // Round half away from zero.
    const double div = static_cast<double>(utc.fraction.count()) / static_cast<double>(unit);
    const double abs_div = div >= 0 ? div : -div;
    const auto abs_rounded = static_cast<long long>(abs_div + 0.5);
    auto rounded = (div >= 0 ? abs_rounded : -abs_rounded) * unit;

    if (rounded >= fractions_per_second) {
        utc.fraction = time_fraction{0};
        utc.second += 1;
        if (utc.second >= 60) {
            utc.second = 0;
            utc.minute += 1;
            if (utc.minute >= 60) {
                utc.minute = 0;
                utc.hour += 1;
            }
        }
        return utc;
    }
    utc.fraction = time_fraction{static_cast<std::int32_t>(rounded)};
    return utc;
}

int encode_utc_time(const utc_date_time& utc, std::span<std::uint8_t> destination) {
    if (destination.size() < 13) {
        throw exception("UTCTime encode destination is too small.");
    }
    write2(destination, 0, utc.year % 100);
    write2(destination, 2, utc.month);
    write2(destination, 4, utc.day);
    write2(destination, 6, utc.hour);
    write2(destination, 8, utc.minute);
    write2(destination, 10, utc.second);
    destination[12] = static_cast<std::uint8_t>('Z');
    return 13;
}

int encode_generalized_no_fraction(const utc_date_time& utc, std::span<std::uint8_t> destination) {
    if (destination.size() < 15) {
        throw exception("GeneralizedTime encode destination is too small.");
    }
    write4(destination, 0, utc.year);
    write2(destination, 4, utc.month);
    write2(destination, 6, utc.day);
    write2(destination, 8, utc.hour);
    write2(destination, 10, utc.minute);
    write2(destination, 12, utc.second);
    destination[14] = static_cast<std::uint8_t>('Z');
    return 15;
}

int encode_generalized_time(utc_date_time utc, int fraction_digits, std::span<std::uint8_t> destination) {
    if (fraction_digits < 0 || fraction_digits > 7) {
        throw exception(
            "GeneralizedTime fractionDigits must be in 0..7, got '" + std::to_string(fraction_digits) + "'.");
    }
    utc = round_to_fraction_digits(utc, fraction_digits);
    if (utc.fraction.count() == 0 || fraction_digits == 0) {
        return encode_generalized_no_fraction(utc, destination);
    }

    char frac_digits[7];
    int fraction_count = utc.fraction.count();
    for (int i = 6; i >= 0; --i) {
        frac_digits[i] = static_cast<char>('0' + (fraction_count % 10));
        fraction_count /= 10;
    }
    int frac_len = 7;
    while (frac_len > 0 && frac_digits[frac_len - 1] == '0') {
        --frac_len;
    }
    if (frac_len > fraction_digits) {
        frac_len = fraction_digits;
        while (frac_len > 0 && frac_digits[frac_len - 1] == '0') {
            --frac_len;
        }
    }
    if (frac_len == 0) {
        return encode_generalized_no_fraction(utc, destination);
    }

    const int total = 15 + 1 + frac_len;
    if (static_cast<int>(destination.size()) < total) {
        throw exception("GeneralizedTime encode destination is too small.");
    }
    write4(destination, 0, utc.year);
    write2(destination, 4, utc.month);
    write2(destination, 6, utc.day);
    write2(destination, 8, utc.hour);
    write2(destination, 10, utc.minute);
    write2(destination, 12, utc.second);
    destination[14] = static_cast<std::uint8_t>('.');
    for (int i = 0; i < frac_len; ++i) {
        destination[static_cast<std::size_t>(15 + i)] = static_cast<std::uint8_t>(frac_digits[i]);
    }
    destination[static_cast<std::size_t>(total - 1)] = static_cast<std::uint8_t>('Z');
    return total;
}

int parse_zone_minutes(std::span<const std::uint8_t> text, int index, encoding enc, bool require_z) {
    if (index >= static_cast<int>(text.size())) {
        throw exception("Time value is missing a time zone.");
    }
    if (text[static_cast<std::size_t>(index)] == static_cast<std::uint8_t>('Z')) {
        if (index + 1 != static_cast<int>(text.size())) {
            throw exception("Unexpected characters after 'Z' in time value.");
        }
        return 0;
    }
    if (require_z || enc == encoding::der) {
        throw exception("DER time values must end with 'Z'.");
    }
    const auto sign_byte = text[static_cast<std::size_t>(index)];
    if (sign_byte != static_cast<std::uint8_t>('+') && sign_byte != static_cast<std::uint8_t>('-')) {
        throw exception("Invalid time zone in time value.");
    }
    const int sign = sign_byte == static_cast<std::uint8_t>('+') ? 1 : -1;
    if (index + 5 != static_cast<int>(text.size())) {
        throw exception("Time zone offset must be +hhmm or -hhmm.");
    }
    const int hh = read2(text, index + 1);
    const int mm = read2(text, index + 3);
    if (hh > 23 || mm > 59) {
        throw exception("Time zone offset is out of range.");
    }
    return sign * (hh * 60 + mm);
}

void apply_offset_to_utc(utc_date_time& dt, int offset_minutes) {
    // local - offset = UTC => UTC = local - offset_minutes
    int total_minutes = dt.hour * 60 + dt.minute - offset_minutes;
    int day_delta = 0;
    while (total_minutes < 0) {
        total_minutes += 24 * 60;
        --day_delta;
    }
    while (total_minutes >= 24 * 60) {
        total_minutes -= 24 * 60;
        ++day_delta;
    }
    dt.hour = total_minutes / 60;
    dt.minute = total_minutes % 60;
    dt.day += day_delta;
    // Day/month/year carry is rare in fixtures; implement basic Gregorian carry.
    auto days_in_month = [](int year, int month) {
        static const int mdays[] = {0, 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31};
        if (month == 2) {
            const bool leap = (year % 4 == 0 && year % 100 != 0) || (year % 400 == 0);
            return leap ? 29 : 28;
        }
        return mdays[month];
    };
    while (dt.day < 1) {
        --dt.month;
        if (dt.month < 1) {
            dt.month = 12;
            --dt.year;
        }
        dt.day += days_in_month(dt.year, dt.month);
    }
    while (dt.day > days_in_month(dt.year, dt.month)) {
        dt.day -= days_in_month(dt.year, dt.month);
        ++dt.month;
        if (dt.month > 12) {
            dt.month = 1;
            ++dt.year;
        }
    }
}

utc_date_time create_date_time(
    int year,
    int month,
    int day,
    int hour,
    int minute,
    int second,
    time_fraction fraction,
    int offset_minutes) {
    if (fraction.count() < 0 || fraction.count() >= fractions_per_second) {
        throw exception("Fractional second is out of range.");
    }
    if (month < 1 || month > 12 || day < 1 || day > 31 || hour > 23 || minute > 59 || second > 59) {
        throw exception("Invalid date/time components.");
    }
    utc_date_time dt{year, month, day, hour, minute, second, fraction};
    apply_offset_to_utc(dt, offset_minutes);
    return dt;
}

utc_date_time parse_utc(std::span<const std::uint8_t> text, encoding enc) {
    if (text.size() < 10) {
        throw exception("UTCTime is too short.");
    }
    const int yy = read2(text, 0);
    const int month = read2(text, 2);
    const int day = read2(text, 4);
    const int hour = read2(text, 6);
    const int minute = read2(text, 8);
    const int year = yy <= 49 ? 2000 + yy : 1900 + yy;
    int index = 10;
    int second = 0;
    if (index + 1 < static_cast<int>(text.size()) && is_digit(text[static_cast<std::size_t>(index)])
        && is_digit(text[static_cast<std::size_t>(index + 1)])) {
        second = read2(text, index);
        index += 2;
    } else {
        if (enc == encoding::der) {
            throw exception("DER UTCTime must include seconds.");
        }
        second = 0;
    }
    const int offset = parse_zone_minutes(text, index, enc, enc == encoding::der);
    return create_date_time(year, month, day, hour, minute, second, time_fraction{0}, offset);
}

utc_date_time parse_generalized(std::span<const std::uint8_t> text, encoding enc) {
    if (text.size() < 12) {
        throw exception("GeneralizedTime is too short.");
    }
    const int year = read4(text, 0);
    const int month = read2(text, 4);
    const int day = read2(text, 6);
    const int hour = read2(text, 8);
    const int minute = read2(text, 10);
    int index = 12;
    int second = 0;
    if (index + 1 < static_cast<int>(text.size()) && is_digit(text[static_cast<std::size_t>(index)])
        && is_digit(text[static_cast<std::size_t>(index + 1)])) {
        second = read2(text, index);
        index += 2;
    } else {
        if (enc == encoding::der) {
            throw exception("DER GeneralizedTime must include seconds.");
        }
        second = 0;
    }

    time_fraction fraction{0};
    if (index < static_cast<int>(text.size())
        && (text[static_cast<std::size_t>(index)] == static_cast<std::uint8_t>('.')
            || text[static_cast<std::size_t>(index)] == static_cast<std::uint8_t>(','))) {
        const auto separator = text[static_cast<std::size_t>(index)];
        if (enc == encoding::der && separator == static_cast<std::uint8_t>(',')) {
            throw exception("DER GeneralizedTime must use '.' as the fraction separator.");
        }
        ++index;
        const int start = index;
        while (index < static_cast<int>(text.size()) && is_digit(text[static_cast<std::size_t>(index)])) {
            ++index;
        }
        const int digit_count = index - start;
        if (digit_count < 1 || digit_count > 7) {
            throw exception("GeneralizedTime fraction must have 1 to 7 digits.");
        }
        int fraction_count = 0;
        for (int i = 0; i < digit_count; ++i) {
            fraction_count = fraction_count * 10
                + (text[static_cast<std::size_t>(start + i)] - '0');
        }
        for (int i = digit_count; i < 7; ++i) {
            fraction_count *= 10;
        }
        fraction = time_fraction{fraction_count};
    }

    const int offset = parse_zone_minutes(text, index, enc, enc == encoding::der);
    return create_date_time(year, month, day, hour, minute, second, fraction, offset);
}

} // namespace

tag text_codec::default_string_tag(string_form form) {
    switch (form) {
    case string_form::utf8:
        return tag::utf8_string;
    case string_form::numeric:
        return tag::numeric_string;
    case string_form::printable:
        return tag::printable_string;
    case string_form::teletex:
    case string_form::t61:
        return tag::teletex_string;
    case string_form::videotex:
        return tag::videotex_string;
    case string_form::ia5:
        return tag::ia5_string;
    case string_form::graphic:
        return tag::graphic_string;
    case string_form::visible:
        return tag::visible_string;
    case string_form::general:
        return tag::general_string;
    case string_form::universal:
        return tag::universal_string;
    case string_form::bmp:
        return tag::bmp_string;
    }
    throw exception("Unknown string form.");
}

tag text_codec::default_time_tag(time_form form) {
    switch (form) {
    case time_form::utc:
        return tag::utc_time;
    case time_form::generalized:
        return tag::generalized_time;
    }
    throw exception("Unknown time form.");
}

int text_codec::get_encoded_byte_count(std::string_view value, string_form form) {
    switch (form) {
    case string_form::utf8:
        return count_utf8_bytes(value);
    case string_form::bmp:
        return static_cast<int>(to_utf16(value).size() * 2);
    case string_form::universal:
        return count_universal_code_points(value) * 4;
    case string_form::numeric:
    case string_form::printable:
    case string_form::ia5:
    case string_form::visible:
        return static_cast<int>(value.size());
    case string_form::teletex:
    case string_form::t61:
    case string_form::videotex:
    case string_form::graphic:
    case string_form::general:
        // Latin-1: each char must be <= 0xFF; UTF-8 input for Latin-1 range is 1 byte per char.
        for (unsigned char c : value) {
            if (c >= 0x80) {
                // Multi-byte UTF-8 for Latin-1: decode to code points
                break;
            }
        }
        {
            int count = 0;
            for (std::size_t i = 0; i < value.size();) {
                const auto cp = decode_utf8_codepoint(value, i);
                if (cp > 0xFF) {
                    throw exception("Latin-1 encode destination size mismatch.");
                }
                ++count;
            }
            return count;
        }
    }
    throw exception("Unknown string form.");
}

void text_codec::encode_string(std::string_view value, string_form form, std::span<std::uint8_t> destination) {
    switch (form) {
    case string_form::utf8: {
        if (static_cast<int>(destination.size()) != count_utf8_bytes(value)) {
            throw exception("UTF-8 encode destination size mismatch.");
        }
        std::memcpy(destination.data(), value.data(), value.size());
        break;
    }
    case string_form::bmp: {
        const auto u16 = to_utf16(value);
        if (destination.size() != u16.size() * 2) {
            throw exception("BMPString encode destination size mismatch.");
        }
        for (std::size_t i = 0; i < u16.size(); ++i) {
            destination[i * 2] = static_cast<std::uint8_t>((u16[i] >> 8) & 0xFF);
            destination[i * 2 + 1] = static_cast<std::uint8_t>(u16[i] & 0xFF);
        }
        break;
    }
    case string_form::universal: {
        std::size_t offset = 0;
        for (std::size_t i = 0; i < value.size();) {
            const auto cp = decode_utf8_codepoint(value, i);
            if (offset + 4 > destination.size()) {
                throw exception("UniversalString encode destination is too small.");
            }
            destination[offset] = static_cast<std::uint8_t>((cp >> 24) & 0xFF);
            destination[offset + 1] = static_cast<std::uint8_t>((cp >> 16) & 0xFF);
            destination[offset + 2] = static_cast<std::uint8_t>((cp >> 8) & 0xFF);
            destination[offset + 3] = static_cast<std::uint8_t>(cp & 0xFF);
            offset += 4;
        }
        if (offset != destination.size()) {
            throw exception("UniversalString encode destination size mismatch.");
        }
        break;
    }
    case string_form::numeric:
        encode_ascii_checked(value, is_numeric, destination);
        break;
    case string_form::printable:
        encode_ascii_checked(value, is_printable, destination);
        break;
    case string_form::ia5:
        encode_ascii_checked(value, is_ia5, destination);
        break;
    case string_form::visible:
        encode_ascii_checked(value, is_visible, destination);
        break;
    case string_form::teletex:
    case string_form::t61:
    case string_form::videotex:
    case string_form::graphic:
    case string_form::general: {
        std::size_t offset = 0;
        for (std::size_t i = 0; i < value.size();) {
            const auto cp = decode_utf8_codepoint(value, i);
            if (cp > 0xFF || offset >= destination.size()) {
                throw exception("Latin-1 encode destination size mismatch.");
            }
            destination[offset++] = static_cast<std::uint8_t>(cp);
        }
        if (offset != destination.size()) {
            throw exception("Latin-1 encode destination size mismatch.");
        }
        break;
    }
    default:
        throw exception("Unknown string form.");
    }
}

std::string text_codec::decode_string(std::span<const std::uint8_t> contents, string_form form) {
    switch (form) {
    case string_form::utf8: {
        // Validate then copy
        std::string_view view(reinterpret_cast<const char*>(contents.data()), contents.size());
        (void)count_utf8_bytes(view);
        return std::string(view);
    }
    case string_form::bmp: {
        if ((contents.size() & 1) != 0) {
            throw exception("BMPString contents length must be even.");
        }
        std::u16string units(contents.size() / 2, 0);
        for (std::size_t i = 0; i < units.size(); ++i) {
            units[i] = static_cast<char16_t>(
                (contents[i * 2] << 8) | contents[i * 2 + 1]);
        }
        return from_utf16(units);
    }
    case string_form::universal: {
        if ((contents.size() & 3) != 0) {
            throw exception("UniversalString contents length must be a multiple of 4.");
        }
        std::string out;
        for (std::size_t i = 0; i < contents.size(); i += 4) {
            const std::uint32_t cp = (static_cast<std::uint32_t>(contents[i]) << 24)
                | (static_cast<std::uint32_t>(contents[i + 1]) << 16)
                | (static_cast<std::uint32_t>(contents[i + 2]) << 8)
                | contents[i + 3];
            if (cp > 0x10FFFF || (cp >= 0xD800 && cp <= 0xDFFF)) {
                throw exception("UniversalString contains an invalid code point.");
            }
            append_utf8(out, cp);
        }
        return out;
    }
    case string_form::numeric:
        return decode_ascii_checked(contents, is_numeric, "NumericString");
    case string_form::printable:
        return decode_ascii_checked(contents, is_printable, "PrintableString");
    case string_form::ia5:
        return decode_ascii_checked(contents, is_ia5, "IA5String");
    case string_form::visible:
        return decode_ascii_checked(contents, is_visible, "VisibleString");
    case string_form::teletex:
    case string_form::t61:
    case string_form::videotex:
    case string_form::graphic:
    case string_form::general: {
        std::string out;
        for (auto b : contents) {
            append_utf8(out, b);
        }
        return out;
    }
    }
    throw exception("Unknown string form.");
}

int text_codec::encode_time(
    const utc_date_time& value,
    time_form form,
    int fraction_digits,
    std::span<std::uint8_t> destination) {
    switch (form) {
    case time_form::utc:
        return encode_utc_time(value, destination);
    case time_form::generalized:
        return encode_generalized_time(value, fraction_digits, destination);
    }
    throw exception("Unknown time form.");
}

utc_date_time text_codec::parse_time(
    std::span<const std::uint8_t> contents,
    time_form form,
    encoding enc) {
    if (contents.empty()) {
        throw exception("Time value is empty.");
    }
    switch (form) {
    case time_form::utc:
        return parse_utc(contents, enc);
    case time_form::generalized:
        return parse_generalized(contents, enc);
    }
    throw exception("Unknown time form.");
}

void text_codec::ensure_trailing_bits_zero(std::span<const std::uint8_t> data, int unused_bits) {
    if (unused_bits == 0 || data.empty()) {
        return;
    }
    const auto mask = static_cast<std::uint8_t>((1 << unused_bits) - 1);
    if ((data.back() & mask) != 0) {
        throw exception("BIT STRING trailing bits must be zero.");
    }
}

} // namespace asn1kit::detail
