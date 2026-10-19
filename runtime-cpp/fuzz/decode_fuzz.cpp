// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

/// Bounded libFuzzer target for soft-profile BER decode.
/// Build with ASN1KIT_BUILD_FUZZ=ON and a Clang toolchain that provides -fsanitize=fuzzer.
/// Not part of the default test gate; promote interesting reject cases into
/// runtime-csharp/fixtures/ber-der/hostile-ber.json.

#include "asn1kit/any.hpp"
#include "asn1kit/reader.hpp"
#include "asn1kit/tag.hpp"

#include <cstddef>
#include <cstdint>
#include <span>
#include <stdexcept>

using namespace asn1kit;

namespace {

reader_options fuzz_options() {
    reader_options options = reader_options::default_profile();
    // Tighter than production defaults so the fuzzer hits limits sooner.
    options.max_recursion_depth = 32;
    options.max_materialized_constructed_value_bytes = 1u * 1024u * 1024u;
    options.max_constructed_segments = 1024;
    return options;
}

void try_decode(std::span<const std::uint8_t> input) {
    reader r(input, encoding::ber, fuzz_options());
    while (!r.eof()) {
        const auto value = r.read_any();
        if (value.tag_value().matches_ignore_constructed(tag::octet_string)) {
            try {
                reader nested(value.encoded(), encoding::ber, fuzz_options());
                (void)nested.read_octet_string(tag::octet_string);
            } catch (const exception&) {
            }
        } else if (value.tag_value().matches_ignore_constructed(tag::bit_string)) {
            try {
                reader nested(value.encoded(), encoding::ber, fuzz_options());
                (void)nested.read_bit_string(tag::bit_string);
            } catch (const exception&) {
            }
        } else if (value.tag_value().matches_ignore_constructed(tag::sequence)) {
            try {
                reader nested(value.encoded(), encoding::ber, fuzz_options());
                nested.with_sequence(tag::sequence, [](reader& inner) {
                    while (!inner.eof()) {
                        (void)inner.read_any();
                    }
                });
            } catch (const exception&) {
            }
        }
    }
}

} // namespace

extern "C" int LLVMFuzzerTestOneInput(const std::uint8_t* data, std::size_t size) {
    if (data == nullptr || size == 0) {
        return 0;
    }
    try {
        try_decode(std::span<const std::uint8_t>{data, size});
    } catch (const exception&) {
    } catch (const std::invalid_argument&) {
    } catch (const std::logic_error&) {
    }
    return 0;
}
