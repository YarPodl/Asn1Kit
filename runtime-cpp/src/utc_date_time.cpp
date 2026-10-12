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
    // Accept: YYYY-MM-DDTHH:MM:SS[.fffffffff]Z
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
        int frac = 0;
        const std::size_t take = digits.size() > 7 ? 7 : digits.size();
        for (std::size_t i = 0; i < take; ++i) {
            frac = frac * 10 + (digits[i] - '0');
        }
        for (std::size_t i = take; i < 7; ++i) {
            frac *= 10;
        }
        dt.fraction = time_fraction{frac};
    }
    if (index != text.size() - 1 || text.back() != 'Z') {
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

utc_time_point utc_date_time::to_sys_time() const {
    namespace ch = std::chrono;
    const auto ymd =
        ch::year{year} / ch::month{static_cast<unsigned>(month)} / ch::day{static_cast<unsigned>(day)};
    const auto day_duration =
        ch::duration_cast<utc_time_point::duration>(ch::sys_days{ymd}.time_since_epoch());
    const auto time_of_day = ch::duration_cast<utc_time_point::duration>(ch::hours{hour})
        + ch::duration_cast<utc_time_point::duration>(ch::minutes{minute})
        + ch::duration_cast<utc_time_point::duration>(ch::seconds{second})
        + ch::duration_cast<utc_time_point::duration>(fraction);
    return utc_time_point{day_duration + time_of_day};
}

utc_date_time utc_date_time::from_sys_time(utc_time_point tp) {
    namespace ch = std::chrono;
    const auto day = ch::floor<ch::days>(tp);
    const auto ymd = ch::year_month_day{day};
    const auto time_of_day = ch::duration_cast<utc_time_point::duration>(tp - day);
    const auto hms = ch::hh_mm_ss<utc_time_point::duration>{time_of_day};
    utc_date_time dt;
    dt.year = static_cast<int>(ymd.year());
    dt.month = static_cast<int>(static_cast<unsigned>(ymd.month()));
    dt.day = static_cast<int>(static_cast<unsigned>(ymd.day()));
    dt.hour = static_cast<int>(hms.hours().count());
    dt.minute = static_cast<int>(hms.minutes().count());
    dt.second = static_cast<int>(hms.seconds().count());
    dt.fraction = time_fraction{static_cast<std::int32_t>(hms.subseconds().count())};
    return dt;
}

std::chrono::system_clock::time_point utc_date_time::to_system_clock() const {
    return std::chrono::time_point_cast<std::chrono::system_clock::duration>(to_sys_time());
}

utc_date_time utc_date_time::from_system_clock(std::chrono::system_clock::time_point tp) {
    return from_sys_time(std::chrono::time_point_cast<utc_time_point::duration>(tp));
}

} // namespace asn1kit
