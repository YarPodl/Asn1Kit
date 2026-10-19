// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "ber_der_fixtures.hpp"

#include "asn1kit/any.hpp"
#include "asn1kit/bit_string.hpp"
#include "asn1kit/exception.hpp"
#include "asn1kit/integer.hpp"
#include "asn1kit/null.hpp"
#include "asn1kit/oid.hpp"
#include "asn1kit/reader.hpp"
#include "asn1kit/tag.hpp"
#include "asn1kit/writer.hpp"

#include <gtest/gtest.h>

#include <array>
#include <cctype>
#include <functional>
#include <iterator>
#include <stdexcept>

using namespace asn1kit;
using namespace asn1kit::test;

namespace {

string_form parse_string_form(const std::string& form) {
    std::string lower;
    lower.reserve(form.size());
    for (char c : form) {
        lower.push_back(static_cast<char>(std::tolower(static_cast<unsigned char>(c))));
    }
    if (lower == "utf8") return string_form::utf8;
    if (lower == "printable") return string_form::printable;
    if (lower == "teletex") return string_form::teletex;
    if (lower == "t61") return string_form::t61;
    if (lower == "ia5") return string_form::ia5;
    if (lower == "numeric") return string_form::numeric;
    if (lower == "visible") return string_form::visible;
    if (lower == "bmp") return string_form::bmp;
    if (lower == "universal") return string_form::universal;
    if (lower == "general") return string_form::general;
    if (lower == "graphic") return string_form::graphic;
    if (lower == "videotex") return string_form::videotex;
    throw std::runtime_error("Unknown string form '" + form + "'.");
}

time_form parse_time_form(const std::string& form) {
    std::string lower;
    lower.reserve(form.size());
    for (char c : form) {
        lower.push_back(static_cast<char>(std::tolower(static_cast<unsigned char>(c))));
    }
    if (lower == "utc") return time_form::utc;
    if (lower == "generalized") return time_form::generalized;
    throw std::runtime_error("Unknown time form '" + form + "'.");
}

using encode_fn = std::function<void(const ber_der_case&, writer&)>;
using decode_fn = std::function<void(const ber_der_case&, reader&)>;

void run_case(const ber_der_case& c, const encode_fn& encode, const decode_fn& decode) {
    const auto expected = c.get_bytes();
    const auto options = c.get_reader_options();
    if (c.reject) {
        reader r(expected, c.encoding_rules(), options);
        EXPECT_THROW(decode(c, r), exception) << c.name;
        return;
    }

    if (c.should_encode()) {
        writer w(encoding::der);
        encode(c, w);
        const auto encoded = w.encode();
        EXPECT_EQ(format_hex(expected), format_hex(encoded)) << c.name;

        writer round_trip(encoding::der);
        encode(c, round_trip);
        EXPECT_EQ(encoded, round_trip.encode()) << c.name;
    }

    reader decode_reader(expected, c.encoding_rules(), options);
    decode(c, decode_reader);
    EXPECT_TRUE(decode_reader.eof()) << c.name;
}

void encode_boolean(const ber_der_case& c, writer& w) {
    w.write_boolean(tag::boolean, get_boolean(c));
}

void decode_boolean(const ber_der_case& c, reader& r) {
    const bool value = r.read_boolean(tag::boolean);
    if (c.has_value()) {
        EXPECT_EQ(get_boolean(c), value) << c.name;
    }
}

void encode_null(const ber_der_case&, writer& w) {
    w.write_null(tag::null);
}

void decode_null(const ber_der_case&, reader& r) {
    r.read_null(tag::null);
}

void encode_integer(const ber_der_case& c, writer& w) {
    w.write_integer_decimal(tag::integer, get_integer_decimal(c));
}

void decode_integer(const ber_der_case& c, reader& r) {
    const auto value = r.read_integer_decimal(tag::integer);
    if (c.has_value()) {
        EXPECT_EQ(get_integer_decimal(c), value) << c.name;
    }
}

void encode_enumerated(const ber_der_case& c, writer& w) {
    w.write_enumerated_decimal(tag::enumerated, get_integer_decimal(c));
}

void decode_enumerated(const ber_der_case& c, reader& r) {
    const auto value = r.read_enumerated_decimal(tag::enumerated);
    if (c.has_value()) {
        EXPECT_EQ(get_integer_decimal(c), value) << c.name;
    }
}

void encode_octet(const ber_der_case& c, writer& w) {
    const auto payload = get_octet_value(c);
    w.write_octet_string(tag::octet_string, payload);
}

void decode_octet(const ber_der_case& c, reader& r) {
    const auto value = r.read_octet_string(tag::octet_string);
    if (c.has_value()) {
        EXPECT_EQ(get_octet_value(c), value.to_vector()) << c.name;
    }
}

void encode_oid(const ber_der_case& c, writer& w) {
    w.write_object_identifier(tag::object_identifier, get_string(c));
}

void decode_oid(const ber_der_case& c, reader& r) {
    const auto value = r.read_object_identifier(tag::object_identifier);
    if (c.has_value()) {
        EXPECT_EQ(get_string(c), value) << c.name;
    }
}

void encode_bit_string(const ber_der_case& c, writer& w) {
    const auto payload = get_octet_value(c);
    const int unused = c.unused_bits.value_or(0);
    w.write_bit_string(tag::bit_string, bit_string::copy_from(payload, unused));
}

void decode_bit_string(const ber_der_case& c, reader& r) {
    const auto decoded = r.read_bit_string(tag::bit_string);
    if (c.has_value()) {
        EXPECT_EQ(c.unused_bits.value_or(0), decoded.unused_bits()) << c.name;
        EXPECT_EQ(get_octet_value(c), decoded.to_vector()) << c.name;
    }
}

void encode_string(const ber_der_case& c, writer& w) {
    const auto form = parse_string_form(*c.form);
    w.write_string(default_string_tag(form), get_string(c), form);
}

void decode_string(const ber_der_case& c, reader& r) {
    const auto form = parse_string_form(*c.form);
    const auto value = r.read_string(default_string_tag(form), form);
    if (c.has_value()) {
        EXPECT_EQ(get_string(c), value) << c.name;
    }
}

void encode_time(const ber_der_case& c, writer& w) {
    const auto form = parse_time_form(*c.form);
    const int digits = c.fraction_digits.value_or(3);
    w.write_time(default_time_tag(form), get_time(c), form, digits);
}

void decode_time(const ber_der_case& c, reader& r) {
    const auto form = parse_time_form(*c.form);
    const auto value = r.read_time(default_time_tag(form), form);
    if (c.has_value()) {
        EXPECT_EQ(get_time(c), value) << c.name;
    }
}

class PrimitiveCodecFixture : public ::testing::TestWithParam<ber_der_case> {};

TEST_P(PrimitiveCodecFixture, Run) {
    const auto& c = GetParam();
    if (c.op == "boolean") {
        run_case(c, encode_boolean, decode_boolean);
    } else if (c.op == "null") {
        run_case(c, encode_null, decode_null);
    } else if (c.op == "integer") {
        run_case(c, encode_integer, decode_integer);
    } else if (c.op == "enumerated") {
        run_case(c, encode_enumerated, decode_enumerated);
    } else if (c.op == "octetString") {
        run_case(c, encode_octet, decode_octet);
    } else if (c.op == "oid") {
        run_case(c, encode_oid, decode_oid);
    } else if (c.op == "bitString") {
        run_case(c, encode_bit_string, decode_bit_string);
    } else if (c.op == "string") {
        run_case(c, encode_string, decode_string);
    } else if (c.op == "time") {
        run_case(c, encode_time, decode_time);
    } else {
        FAIL() << "Unsupported op '" << c.op << "' in " << c.name;
    }
}

std::vector<ber_der_case> all_layer1_cases() {
    std::vector<ber_der_case> all;
    for (const char* file : {
             "boolean.json",
             "null.json",
             "integer.json",
             "enumerated.json",
             "octet-string.json",
             "oid.json",
             "bit-string.json",
             "string.json",
             "time.json",
         }) {
        auto cases = load_cases(file);
        all.insert(all.end(), std::make_move_iterator(cases.begin()), std::make_move_iterator(cases.end()));
    }
    return all;
}

INSTANTIATE_TEST_SUITE_P(
    BerDerFixtures,
    PrimitiveCodecFixture,
    ::testing::ValuesIn(all_layer1_cases()),
    [](const ::testing::TestParamInfo<ber_der_case>& info) {
        std::string name = info.param.name;
        for (char& c : name) {
            if (!std::isalnum(static_cast<unsigned char>(c))) {
                c = '_';
            }
        }
        return name;
    });

TEST(PrimitiveCodecExtra, SoftInteger_DefaultAccepts_StrictRejects) {
    const auto soft_bytes = parse_hex("02020001");
    EXPECT_EQ("1", reader(soft_bytes, encoding::der).read_integer_decimal(tag::integer));
    EXPECT_THROW(
        reader(soft_bytes, encoding::der, reader_options::strict()).read_integer_decimal(tag::integer),
        exception);

    writer w(encoding::der);
    w.write_integer(tag::integer, 1);
    EXPECT_EQ(parse_hex("020101"), w.encode());
}

TEST(PrimitiveCodecExtra, SoftBitStringTrailing_DefaultAccepts_StrictRejects) {
    const auto soft_bytes = parse_hex("030203A9");
    const auto decoded = reader(soft_bytes, encoding::der).read_bit_string(tag::bit_string);
    EXPECT_EQ(3, decoded.unused_bits());
    EXPECT_EQ(std::vector<std::uint8_t>{0xA9}, decoded.to_vector());
    EXPECT_THROW(
        reader(soft_bytes, encoding::der, reader_options::strict()).read_bit_string(tag::bit_string),
        exception);
}

TEST(PrimitiveCodecExtra, NonMinimalLength_DefaultRejects_AllowProfileAccepts) {
    const auto bytes = parse_hex("02810101");
    EXPECT_THROW(reader(bytes, encoding::der).read_integer_decimal(tag::integer), exception);
    EXPECT_EQ(
        "1",
        reader(bytes, encoding::der, reader_options::allow_non_minimal_length())
            .read_integer_decimal(tag::integer));
}

TEST(PrimitiveCodecExtra, OverlongOid_DefaultRejects_AllowProfileAccepts) {
    const auto bytes = parse_hex("06032A8001");
    EXPECT_THROW(reader(bytes, encoding::der).read_object_identifier(tag::object_identifier), exception);
    EXPECT_EQ(
        "1.2.1",
        reader(bytes, encoding::der, reader_options::allow_overlong_oid_base128())
            .read_object_identifier(tag::object_identifier));
}

TEST(PrimitiveCodecExtra, HostileBer_IndefiniteScanEnforcesRecursionDepth) {
    const auto encoded = parse_hex("2480248004014100000000");

    auto allowed_options = reader_options::default_profile();
    allowed_options.max_recursion_depth = 2;
    reader allowed(encoded, encoding::ber, allowed_options);
    EXPECT_EQ((std::vector<std::uint8_t>{0x41}), allowed.read_octet_string(tag::octet_string).to_vector());

    auto rejected_options = reader_options::default_profile();
    rejected_options.max_recursion_depth = 1;
    reader rejected(encoded, encoding::ber, rejected_options);
    EXPECT_THROW((void)rejected.read_any(), exception);
}

TEST(PrimitiveCodecExtra, HostileBer_RejectsZeroRecursionDepth) {
    auto options = reader_options::default_profile();
    options.max_recursion_depth = 0;
    EXPECT_THROW(reader(parse_hex("0500"), encoding::ber, options), std::invalid_argument);
}

TEST(PrimitiveCodecExtra, HostileBer_DefiniteSequenceNestingEnforcesRecursionDepth) {
    // SEQUENCE { SEQUENCE { INTEGER 1 } }
    const auto nested = parse_hex("30053003020101");

    auto allowed_options = reader_options::default_profile();
    allowed_options.max_recursion_depth = 2;
    reader allowed(nested, encoding::der, allowed_options);
    allowed.with_sequence(tag::sequence, [&](reader& outer) {
        outer.with_sequence(tag::sequence, [&](reader& inner) {
            EXPECT_EQ(1, inner.read_int32(tag::integer));
        });
    });

    auto rejected_options = reader_options::default_profile();
    rejected_options.max_recursion_depth = 1;
    reader rejected(nested, encoding::der, rejected_options);
    EXPECT_THROW(
        rejected.with_sequence(tag::sequence, [&](reader& outer) {
            outer.with_sequence(tag::sequence, [&](reader&) {});
        }),
        exception);
}

TEST(PrimitiveCodecExtra, HostileBer_ConstructedOctetEnforcesSegmentLimit) {
    const auto ber = parse_hex("2406040141040142");

    auto allowed_options = reader_options::default_profile();
    allowed_options.max_constructed_segments = 2;
    reader allowed(ber, encoding::ber, allowed_options);
    EXPECT_EQ(
        (std::vector<std::uint8_t>{0x41, 0x42}),
        allowed.read_octet_string(tag::octet_string).to_vector());

    auto rejected_options = reader_options::default_profile();
    rejected_options.max_constructed_segments = 1;
    reader rejected(ber, encoding::ber, rejected_options);
    EXPECT_THROW((void)rejected.read_octet_string(tag::octet_string), exception);

    auto empty_options = reader_options::default_profile();
    empty_options.max_constructed_segments = 0;
    reader empty_only(parse_hex("2400"), encoding::ber, empty_options);
    EXPECT_TRUE(empty_only.read_octet_string(tag::octet_string).empty());
}

TEST(PrimitiveCodecExtra, HostileBer_ConstructedOctetEnforcesLimitsAndKeepsTryAtomic) {
    const auto encoded = parse_hex("24080401412403040142");

    auto allowed_options = reader_options::default_profile();
    allowed_options.max_materialized_constructed_value_bytes = 2;
    reader allowed(encoded, encoding::ber, allowed_options);
    EXPECT_EQ(
        (std::vector<std::uint8_t>{0x41, 0x42}),
        allowed.read_octet_string(tag::octet_string).to_vector());

    auto recursion_options = allowed_options;
    recursion_options.max_recursion_depth = 1;
    reader recursion_rejected(encoded, encoding::ber, recursion_options);
    EXPECT_THROW((void)recursion_rejected.read_octet_string(tag::octet_string), exception);

    auto size_options = reader_options::default_profile();
    size_options.max_materialized_constructed_value_bytes = 1;
    reader size_rejected(encoded, encoding::ber, size_options);
    std::array<std::uint8_t, 2> destination{};
    std::size_t written = 0;
    EXPECT_THROW(
        (void)size_rejected.try_read_octet_string(tag::octet_string, destination, written),
        exception);
    EXPECT_EQ(encoded.size(), size_rejected.remaining());
}

TEST(PrimitiveCodecExtra, HostileBer_PrimitiveOctetDoesNotConsumeMaterializationBudget) {
    auto options = reader_options::default_profile();
    options.max_materialized_constructed_value_bytes = 0;
    reader r(parse_hex("04024142"), encoding::ber, options);
    EXPECT_EQ((std::vector<std::uint8_t>{0x41, 0x42}), r.read_octet_string(tag::octet_string).to_vector());
}

TEST(PrimitiveCodecExtra, HostileBer_ConstructedBitStringAndStringEnforceMaterializationLimit) {
    auto options = reader_options::default_profile();
    options.max_materialized_constructed_value_bytes = 1;

    reader bits(parse_hex("2308030200A003020500"), encoding::ber, options);
    EXPECT_THROW((void)bits.read_bit_string(tag::bit_string), exception);

    reader text(parse_hex("2C060C01410C0142"), encoding::ber, options);
    EXPECT_THROW((void)text.read_string(tag::utf8_string, string_form::utf8), exception);
}

TEST(PrimitiveCodecExtra, IntegerValue_PreservesNonMinimalContents_OnWrite) {
    const std::uint8_t soft[] = {0x00, 0x01};
    auto value = integer::copy_from(soft);
    EXPECT_EQ(1, value.get_int32());
    writer w(encoding::der);
    w.write_integer(tag::integer, value);
    EXPECT_EQ((std::vector<std::uint8_t>{0x02, 0x02, 0x00, 0x01}), w.encode());
}

TEST(PrimitiveCodecExtra, Writer_Reset_ClearsOutput) {
    writer w(encoding::der);
    w.write_integer(tag::integer, 1);
    EXPECT_EQ((std::vector<std::uint8_t>{0x02, 0x01, 0x01}), w.encode());
    w.reset();
    EXPECT_EQ(0u, w.encoded_length());
    w.write_integer(tag::integer, 2);
    EXPECT_EQ((std::vector<std::uint8_t>{0x02, 0x01, 0x02}), w.encode());
}

TEST(PrimitiveCodecExtra, TryEncode_CopiesWhenDestinationFits) {
    writer w(encoding::der);
    w.write_null(tag::null);
    std::uint8_t dest[8]{};
    std::size_t written = 0;
    EXPECT_TRUE(w.try_encode(dest, written));
    EXPECT_EQ(2u, written);
    EXPECT_EQ(0x05, dest[0]);
    EXPECT_EQ(0x00, dest[1]);
}

TEST(PrimitiveCodecExtra, EveryProperPrefixOfRepresentativeTlvsThrowsDomainException) {
    std::vector<std::uint8_t> long_octet(131, 0xA5);
    long_octet[0] = 0x04;
    long_octet[1] = 0x81;
    long_octet[2] = 0x80;

    struct prefix_case {
        std::vector<std::uint8_t> encoded;
        encoding rules;
        std::function<void(reader&)> decode;
    };
    const std::vector<prefix_case> cases{
        {parse_hex("9F81490100"), encoding::der, [](reader& r) { (void)r.read_any(); }},
        {parse_hex("020101"), encoding::der,
         [](reader& r) { (void)r.read_integer_decimal(tag::integer); }},
        {std::move(long_octet), encoding::der,
         [](reader& r) { (void)r.read_octet_string(tag::octet_string); }},
        {parse_hex("248024800401AA00000000"), encoding::ber,
         [](reader& r) { (void)r.read_octet_string(tag::octet_string); }},
    };

    for (const auto& item : cases) {
        for (std::size_t length = 0; length < item.encoded.size(); ++length) {
            const std::vector<std::uint8_t> prefix(item.encoded.begin(), item.encoded.begin() + length);
            reader truncated(prefix, item.rules);
            EXPECT_THROW(item.decode(truncated), exception)
                << "prefix length " << length << " of " << format_hex(item.encoded);
        }

        reader complete(item.encoded, item.rules);
        item.decode(complete);
        EXPECT_TRUE(complete.eof());
    }
}

TEST(PrimitiveCodecExtra, ConstructedOctetMaterializationDetachesFromBorrowedInput) {
    auto storage = parse_hex("24800403416E6E0000");
    reader r(bytes::borrow(storage), encoding::ber);
    const auto decoded = r.read_octet_string(tag::octet_string);
    ASSERT_EQ((std::vector<std::uint8_t>{0x41, 0x6E, 0x6E}), decoded.to_vector());

    storage[4] = 0x00;
    EXPECT_EQ((std::vector<std::uint8_t>{0x41, 0x6E, 0x6E}), decoded.to_vector());
}

TEST(PrimitiveCodecExtra, StringEncodeRejectsInvalidUtf8BmpAndLatin1Values) {
    writer utf8(encoding::der);
    EXPECT_THROW(utf8.write_string(tag::utf8_string, "\xC0\x80", string_form::utf8), exception);

    writer bmp(encoding::der);
    EXPECT_THROW(
        bmp.write_string(tag::bmp_string, "\xF0\x9F\x98\x80", string_form::bmp),
        exception);

    writer latin1(encoding::der);
    EXPECT_THROW(
        latin1.write_string(tag::teletex_string, "\xC4\x80", string_form::teletex),
        exception);
}

TEST(PrimitiveCodecExtra, GeneralizedTimeRoundingCarriesAcrossCalendarBoundaries) {
    writer year_boundary(encoding::der);
    year_boundary.write_time(
        tag::generalized_time,
        utc_date_time{1999, 12, 31, 23, 59, 59, time_fraction{9'999'999}},
        time_form::generalized,
        6);
    EXPECT_EQ(parse_hex("180F32303030303130313030303030305A"), year_boundary.encode());

    writer leap_boundary(encoding::der);
    leap_boundary.write_time(
        tag::generalized_time,
        utc_date_time{2000, 2, 29, 23, 59, 59, time_fraction{9'999'999}},
        time_form::generalized,
        6);
    EXPECT_EQ(parse_hex("180F32303030303330313030303030305A"), leap_boundary.encode());
}

TEST(PrimitiveCodecExtra, TimeEncodeRejectsInvalidCalendarAndUtcYear) {
    writer invalid_calendar(encoding::der);
    EXPECT_THROW(
        invalid_calendar.write_time(
            tag::generalized_time,
            utc_date_time{2023, 2, 29, 12, 0, 0, time_fraction{0}},
            time_form::generalized,
            3),
        exception);

    for (int year : {1949, 2050}) {
        writer out_of_range(encoding::der);
        EXPECT_THROW(
            out_of_range.write_time(
                tag::utc_time,
                utc_date_time{year, 1, 1, 0, 0, 0, time_fraction{0}},
                time_form::utc,
                3),
            exception)
            << "year=" << year;
    }
}

TEST(PrimitiveCodecExtra, OidEnforcesIntArcDomainAndSupportsManyArcs) {
    EXPECT_THROW((void)oid::parse("1.2.2147483648"), exception);
    EXPECT_THROW((void)oid::parse("2.2147483568"), exception);

    std::string dotted = "1.2";
    for (int i = 0; i < 128; ++i) {
        dotted += ".0";
    }
    const auto value = oid::parse(dotted);
    EXPECT_EQ(dotted, value.to_string());

    writer w(encoding::der);
    w.write_object_identifier(tag::object_identifier, value);
    reader r(w.encode(), encoding::der);
    EXPECT_EQ(dotted, r.read_object_identifier(tag::object_identifier));
    EXPECT_TRUE(r.eof());
}

} // namespace
