// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "asn1kit/utc_date_time.hpp"

#include "asn1kit/exception.hpp"

#include <cctype>
#include <chrono>
#include <sstream>

namespace asn1kit {
namespace {

int parse_int(std::string_view s) {
    int value = 0;
    for (char c : s) {
        if (c < '0' || c > '9') {
            throw exception("Invalid ISO date/time.");
        }
        value = value * 10 + (c - '0');
    }
    return value;
}

} // namespace

utc_date_time utc_date_time::parse_iso(std::string_view text) {
    // Accept: YYYY-MM-DDTHH:MM:SS[.fffffff]Z
    if (text.size() < 20 || text.back() != 'Z') {
        throw exception("Invalid ISO date/time.");
    }
    if (text[4] != '-' || text[7] != '-' || text[10] != 'T' || text[13] != ':' || text[16] != ':') {
        throw exception("Invalid ISO date/time.");
    }
    utc_date_time dt;
    dt.year = parse_int(text.substr(0, 4));
    dt.month = parse_int(text.substr(5, 2));
    dt.day = parse_int(text.substr(8, 2));
    dt.hour = parse_int(text.substr(11, 2));
    dt.minute = parse_int(text.substr(14, 2));
    dt.second = parse_int(text.substr(17, 2));
    dt.fraction = time_fraction{0};

    std::size_t index = 19;
    if (index < text.size() - 1 && text[index] == '.') {
        ++index;
        const std::size_t start = index;
        while (index < text.size() - 1 && std::isdigit(static_cast<unsigned char>(text[index]))) {
            ++index;
        }
        const auto digits = text.substr(start, index - start);
        if (digits.empty() || digits.size() > 7) {
            throw exception("Invalid ISO date/time.");
        }
        int frac = 0;
        for (std::size_t i = 0; i < digits.size(); ++i) {
            frac = frac * 10 + (digits[i] - '0');
        }
        for (std::size_t i = digits.size(); i < 7; ++i) {
            frac *= 10;
        }
        dt.fraction = time_fraction{frac};
    }
    if (index != text.size() - 1 || text.back() != 'Z') {
        throw exception("Invalid ISO date/time.");
    }
    const auto date = std::chrono::year{dt.year}
        / std::chrono::month{static_cast<unsigned>(dt.month)}
        / std::chrono::day{static_cast<unsigned>(dt.day)};
    if (dt.year < 1 || dt.year > 9999 || !date.ok()
        || dt.hour < 0 || dt.hour > 23 || dt.minute < 0 || dt.minute > 59
        || dt.second < 0 || dt.second > 59) {
        throw exception("Invalid ISO date/time.");
    }
    return dt;
}

std::string utc_date_time::to_iso() const {
    std::ostringstream oss;
    oss << (year < 1000 ? "0" : "") << (year < 100 ? "0" : "") << (year < 10 ? "0" : "") << year
        << '-'
        << (month < 10 ? "0" : "") << month << '-'
        << (day < 10 ? "0" : "") << day << 'T'
        << (hour < 10 ? "0" : "") << hour << ':'
        << (minute < 10 ? "0" : "") << minute << ':'
        << (second < 10 ? "0" : "") << second;
    if (fraction.count() != 0) {
        char digits[8];
        int frac = fraction.count();
        for (int i = 6; i >= 0; --i) {
            digits[i] = static_cast<char>('0' + (frac % 10));
            frac /= 10;
        }
        digits[7] = '\0';
        int len = 7;
        while (len > 0 && digits[len - 1] == '0') {
            --len;
        }
        oss << '.';
        oss.write(digits, len);
    }
    oss << 'Z';
    return oss.str();
}

} // namespace asn1kit
