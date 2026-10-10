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

    friend bool operator==(const utc_date_time& a, const utc_date_time& b) noexcept {
        return a.year == b.year && a.month == b.month && a.day == b.day
            && a.hour == b.hour && a.minute == b.minute && a.second == b.second
            && a.fraction == b.fraction;
    }
    friend bool operator!=(const utc_date_time& a, const utc_date_time& b) noexcept {
        return !(a == b);
    }
};

} // namespace asn1kit
