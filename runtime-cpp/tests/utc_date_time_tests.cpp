// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "asn1kit/utc_date_time.hpp"

#include <gtest/gtest.h>

#include <chrono>
#include <cstdint>
#include <initializer_list>
#include <string>

namespace {

using namespace asn1kit;

constexpr utc_date_time constexpr_date{2024, 2, 29, 23, 59, 58, time_fraction{1234567}};
constexpr auto constexpr_time_point = constexpr_date.to_sys_time();
static_assert(utc_date_time::from_sys_time(constexpr_time_point) == constexpr_date);
static_assert(utc_date_time::from_system_clock(constexpr_date.to_system_clock()) == constexpr_date);

TEST(UtcDateTime, SysTimeRoundTrip) {
    // utc_date_time -> sys_time -> utc_date_time must be lossless, including dates
    // far from the epoch (the old 32-bit time_fraction rep overflowed here).
    const utc_date_time cases[] = {
        {1970, 1, 1, 0, 0, 0, time_fraction{0}},            // epoch
        {1970, 1, 1, 0, 0, 0, time_fraction{1}},            // smallest fraction
        {1969, 12, 31, 23, 59, 59, time_fraction{9999999}}, // before epoch
        {2024, 6, 15, 12, 30, 45, time_fraction{1234567}},  // arbitrary mid-range
        {2024, 2, 29, 23, 59, 59, time_fraction{9999999}},  // leap day, max fraction
        {2000, 1, 1, 0, 0, 0, time_fraction{0}},
        {2038, 1, 19, 3, 14, 7, time_fraction{0}}, // classic 32-bit timestamp boundary
    };
    for (const auto& dt : cases) {
        EXPECT_EQ(utc_date_time::from_sys_time(dt.to_sys_time()), dt) << dt.to_iso();
    }
}

TEST(UtcDateTime, SysTimeEpochAndTickScale) {
    const utc_date_time epoch{1970, 1, 1, 0, 0, 0, time_fraction{0}};
    EXPECT_EQ(epoch.to_sys_time().time_since_epoch().count(), 0);

    // 2024-06-15 is 19889 days after the Unix epoch; ticks are 100ns each.
    const utc_date_time dt{2024, 6, 15, 0, 0, 0, time_fraction{0}};
    const std::int64_t expected = 19889LL * 86400LL * 10'000'000LL;
    EXPECT_EQ(dt.to_sys_time().time_since_epoch().count(), expected);
}

TEST(UtcDateTime, SystemClockRoundTrip) {
    const utc_date_time cases[] = {
        {1970, 1, 1, 0, 0, 0, time_fraction{0}},
        {2024, 6, 15, 12, 30, 45, time_fraction{1234567}},
        {2024, 12, 31, 23, 59, 59, time_fraction{9999999}},
    };
    for (const auto& dt : cases) {
        const auto tp = dt.to_system_clock();
        EXPECT_EQ(utc_date_time::from_system_clock(tp), dt) << dt.to_iso();

        // Same instant as the sys_time conversion (both target platforms use
        // a resolution of at least 100ns, so no truncation happens here).
        EXPECT_EQ(tp, std::chrono::time_point_cast<std::chrono::system_clock::duration>(dt.to_sys_time()))
            << dt.to_iso();
    }
}

TEST(UtcDateTime, IsoConsistency) {
    const std::string iso = "2024-06-15T12:30:45.1234567Z";
    const auto dt = utc_date_time::parse_iso(iso);
    EXPECT_EQ(dt.to_iso(), iso);
    EXPECT_EQ(utc_date_time::from_sys_time(dt.to_sys_time()).to_iso(), iso);
    EXPECT_EQ(utc_date_time::from_system_clock(dt.to_system_clock()).to_iso(), iso);
}

TEST(UtcDateTime, FractionPrecision) {
    // time_fraction is 100ns (7 decimal digits); every representable value must survive.
    for (std::int32_t frac : {0, 1, 10, 100, 1000, 10000, 100000, 1000000, 1234567, 9999999}) {
        const utc_date_time dt{2024, 6, 15, 12, 30, 45, time_fraction{frac}};
        const auto round_tripped = utc_date_time::from_sys_time(dt.to_sys_time());
        EXPECT_EQ(round_tripped.fraction.count(), frac) << "frac=" << frac;
        EXPECT_EQ(round_tripped, dt) << "frac=" << frac;
    }
}

TEST(UtcDateTime, SysTimeComponents) {
    const utc_date_time dt{2024, 6, 15, 12, 30, 45, time_fraction{1234567}};
    const auto sys_tp = dt.to_sys_time();

    using namespace std::chrono;
    const auto day = floor<days>(sys_tp);
    const auto ymd = year_month_day{day};
    const auto hms = hh_mm_ss<utc_time_point::duration>{sys_tp - day};

    EXPECT_EQ(static_cast<int>(ymd.year()), dt.year);
    EXPECT_EQ(static_cast<unsigned>(ymd.month()), static_cast<unsigned>(dt.month));
    EXPECT_EQ(static_cast<unsigned>(ymd.day()), static_cast<unsigned>(dt.day));
    EXPECT_EQ(hms.hours().count(), dt.hour);
    EXPECT_EQ(hms.minutes().count(), dt.minute);
    EXPECT_EQ(hms.seconds().count(), dt.second);
    EXPECT_EQ(hms.subseconds().count(), dt.fraction.count());
}

} // namespace
