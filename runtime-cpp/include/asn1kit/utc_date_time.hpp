// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#pragma once

#include <chrono>
#include <cstdint>
#include <ratio>
#include <string>
#include <string_view>

namespace asn1kit {

/// Sub-second duration with at most 7 decimal digits (X.690 GeneralizedTime).
using time_fraction = std::chrono::duration<std::int32_t, std::ratio<1, 10'000'000>>;

/// UTC instant with 100ns resolution and a 64-bit range.
///
/// `time_fraction` only covers the sub-second field, so its 32-bit rep cannot
/// represent a full instant; point-in-time arithmetic needs the wider rep.
using utc_time_point = std::chrono::sys_time<std::chrono::duration<std::int64_t, std::ratio<1, 10'000'000>>>;

/// UTC calendar date/time with optional fractional seconds.
/// Used for UTCTime / GeneralizedTime encode/decode and fixture ISO comparison.
struct utc_date_time {
    int year{0};
    int month{0};
    int day{0};
    int hour{0};
    int minute{0};
    int second{0};
    time_fraction fraction{};

    [[nodiscard]] static utc_date_time parse_iso(std::string_view text);
    [[nodiscard]] std::string to_iso() const;

    // std::chrono conversions (UTC-based, 100ns precision).
    [[nodiscard]] constexpr utc_time_point to_sys_time() const {
        namespace ch = std::chrono;
        const auto ymd = ch::year{year} / ch::month{static_cast<unsigned>(month)}
            / ch::day{static_cast<unsigned>(day)};
        const auto day_duration =
            ch::duration_cast<utc_time_point::duration>(ch::sys_days{ymd}.time_since_epoch());
        const auto time_of_day = ch::duration_cast<utc_time_point::duration>(ch::hours{hour})
            + ch::duration_cast<utc_time_point::duration>(ch::minutes{minute})
            + ch::duration_cast<utc_time_point::duration>(ch::seconds{second})
            + ch::duration_cast<utc_time_point::duration>(fraction);
        return utc_time_point{day_duration + time_of_day};
    }

    [[nodiscard]] static constexpr utc_date_time from_sys_time(utc_time_point tp) {
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

    // Convenience: system_clock conversions (truncate if system_clock is coarser than 100ns).
    [[nodiscard]] constexpr std::chrono::system_clock::time_point to_system_clock() const {
        return std::chrono::time_point_cast<std::chrono::system_clock::duration>(to_sys_time());
    }

    [[nodiscard]] static constexpr utc_date_time from_system_clock(
        std::chrono::system_clock::time_point tp) {
        return from_sys_time(std::chrono::time_point_cast<utc_time_point::duration>(tp));
    }

    friend constexpr bool operator==(const utc_date_time& a, const utc_date_time& b) noexcept {
        return a.year == b.year && a.month == b.month && a.day == b.day
            && a.hour == b.hour && a.minute == b.minute && a.second == b.second
            && a.fraction == b.fraction;
    }
    friend constexpr bool operator!=(const utc_date_time& a, const utc_date_time& b) noexcept {
        return !(a == b);
    }
};

} // namespace asn1kit
