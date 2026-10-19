// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "asn1kit/any.hpp"
#include "asn1kit/exception.hpp"
#include "asn1kit/reader.hpp"
#include "asn1kit/tag.hpp"

#include <gtest/gtest.h>
#include <nlohmann/json.hpp>

#include <cctype>
#include <fstream>
#include <optional>
#include <stdexcept>
#include <string>
#include <vector>

using namespace asn1kit;

namespace {

struct hostile_ber_case {
    std::string name;
    std::string rules;
    std::string op;
    std::string bytes_hex;
    bool reject{false};
    std::optional<std::size_t> max_recursion_depth;
    std::optional<std::size_t> max_constructed_segments;
    std::optional<std::size_t> max_materialized_constructed_value_bytes;
    std::optional<nlohmann::json> value;

    [[nodiscard]] encoding encoding_rules() const {
        return rules == "ber" ? encoding::ber : encoding::der;
    }

    [[nodiscard]] reader_options get_reader_options() const {
        reader_options options = reader_options::default_profile();
        if (max_recursion_depth) {
            options.max_recursion_depth = *max_recursion_depth;
        }
        if (max_constructed_segments) {
            options.max_constructed_segments = *max_constructed_segments;
        }
        if (max_materialized_constructed_value_bytes) {
            options.max_materialized_constructed_value_bytes =
                *max_materialized_constructed_value_bytes;
        }
        return options;
    }
};

std::vector<std::uint8_t> parse_hex(const std::string& hex) {
    std::string cleaned;
    cleaned.reserve(hex.size());
    for (char ch : hex) {
        if (ch != ' ' && ch != '-') {
            cleaned.push_back(ch);
        }
    }
    if (cleaned.size() % 2 != 0) {
        throw std::runtime_error("odd hex length");
    }
    std::vector<std::uint8_t> out;
    out.reserve(cleaned.size() / 2);
    for (std::size_t i = 0; i < cleaned.size(); i += 2) {
        out.push_back(static_cast<std::uint8_t>(std::stoul(cleaned.substr(i, 2), nullptr, 16)));
    }
    return out;
}

std::vector<hostile_ber_case> load_corpus() {
    const std::string path = std::string(ASN1KIT_BER_DER_FIXTURES_DIR) + "/hostile-ber.json";
    std::ifstream in(path);
    if (!in) {
        throw std::runtime_error("Cannot open hostile-ber.json: " + path);
    }
    nlohmann::json root;
    in >> root;
    std::vector<hostile_ber_case> cases;
    for (const auto& item : root) {
        hostile_ber_case c;
        c.name = item.at("name").get<std::string>();
        c.rules = item.value("rules", std::string("der"));
        c.op = item.at("op").get<std::string>();
        c.bytes_hex = item.at("bytes").get<std::string>();
        c.reject = item.value("reject", false);
        if (item.contains("maxRecursionDepth")) {
            c.max_recursion_depth = item.at("maxRecursionDepth").get<std::size_t>();
        }
        if (item.contains("maxConstructedSegments")) {
            c.max_constructed_segments = item.at("maxConstructedSegments").get<std::size_t>();
        }
        if (item.contains("maxMaterializedConstructedValueBytes")) {
            c.max_materialized_constructed_value_bytes =
                item.at("maxMaterializedConstructedValueBytes").get<std::size_t>();
        }
        if (item.contains("value") && !item.at("value").is_null()) {
            c.value = item.at("value");
        }
        cases.push_back(std::move(c));
    }
    if (cases.empty()) {
        throw std::runtime_error("hostile-ber.json has no cases");
    }
    return cases;
}

void consume(reader& r, const hostile_ber_case& c) {
    if (c.op == "nestedSequence") {
        r.with_sequence(tag::sequence, [&](reader& outer) {
            outer.with_sequence(tag::sequence, [&](reader& inner) {
                const auto value = inner.read_int32(tag::integer);
                if (c.value) {
                    EXPECT_EQ(c.value->get<int>(), value);
                }
            });
        });
        return;
    }
    if (c.op == "octetString") {
        const auto value = r.read_octet_string(tag::octet_string);
        if (c.value) {
            EXPECT_EQ(parse_hex(c.value->get<std::string>()), value.to_vector());
        }
        return;
    }
    if (c.op == "any") {
        (void)r.read_any();
        return;
    }
    throw std::runtime_error("Unsupported hostile-BER op '" + c.op + "'");
}

class HostileBerCorpus : public ::testing::TestWithParam<hostile_ber_case> {};

TEST_P(HostileBerCorpus, BoundedDecoderFuzz_CorpusRegressions) {
    const auto& c = GetParam();
    const auto bytes = parse_hex(c.bytes_hex);
    reader r(bytes, c.encoding_rules(), c.get_reader_options());
    if (c.reject) {
        EXPECT_THROW(consume(r, c), exception);
        return;
    }
    consume(r, c);
    r.throw_if_not_empty();
}

INSTANTIATE_TEST_SUITE_P(
    HostileBer,
    HostileBerCorpus,
    ::testing::ValuesIn(load_corpus()),
    [](const ::testing::TestParamInfo<hostile_ber_case>& info) {
        std::string name = info.param.name;
        for (char& ch : name) {
            if (!(std::isalnum(static_cast<unsigned char>(ch)) || ch == '_')) {
                ch = '_';
            }
        }
        return name;
    });

} // namespace
