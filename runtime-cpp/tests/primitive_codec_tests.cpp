// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "ber_der_fixtures.hpp"

#include "asn1kit/bit_string.hpp"
#include "asn1kit/exception.hpp"
#include "asn1kit/integer.hpp"
#include "asn1kit/null.hpp"
#include "asn1kit/oid.hpp"
#include "asn1kit/reader.hpp"
#include "asn1kit/tag.hpp"
#include "asn1kit/writer.hpp"

#include <gtest/gtest.h>

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
    EXPECT_EQ(0, w.encoded_length());
    w.write_integer(tag::integer, 2);
    EXPECT_EQ((std::vector<std::uint8_t>{0x02, 0x01, 0x02}), w.encode());
}

TEST(PrimitiveCodecExtra, TryEncode_CopiesWhenDestinationFits) {
    writer w(encoding::der);
    w.write_null(tag::null);
    std::uint8_t dest[8]{};
    int written = 0;
    EXPECT_TRUE(w.try_encode(dest, written));
    EXPECT_EQ(2, written);
    EXPECT_EQ(0x05, dest[0]);
    EXPECT_EQ(0x00, dest[1]);
}

} // namespace
