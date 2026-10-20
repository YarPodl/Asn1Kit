// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "encode_samples.hpp"
#include "fixture_loader.hpp"
#include "openssl_support.hpp"

#include "PKIX1Explicit2009.hpp"

#include "asn1kit/encode_decode.hpp"
#include "asn1kit/writer.hpp"

#include <benchmark/benchmark.h>

#include <cstdint>
#include <vector>

namespace {

namespace Pkix = asn1kit::modern::PKIX1Explicit2009;

std::vector<std::uint8_t> g_cert_der;
Pkix::Certificate g_encode_sample{};
asn1kit::writer g_encode_writer{asn1kit::encoding::der};
asn1kit::benchmarks::openssl_support::x509_ptr g_openssl_certificate;

void SetupCertificate(const benchmark::State&) {
    g_cert_der = asn1kit::benchmarks::read_pkix_fixture("TrustAnchorRootCertificate.crt");
    g_encode_sample = asn1kit::benchmarks::create_certificate();
    g_encode_writer.ensure_capacity(4096);
    g_openssl_certificate =
        asn1kit::benchmarks::openssl_support::decode_certificate(g_cert_der);
}

void Certificate_Asn1Kit_Decode(benchmark::State& state) {
    for (auto _ : state) {
        auto value = asn1kit::decode<Pkix::Certificate>(g_cert_der, asn1kit::encoding::der);
        benchmark::DoNotOptimize(value);
        benchmark::ClobberMemory();
    }
}

void Certificate_Asn1Kit_Encode(benchmark::State& state) {
    for (auto _ : state) {
        g_encode_writer.reset();
        g_encode_sample.encode(g_encode_writer);
        auto encoded = g_encode_writer.encode();
        benchmark::DoNotOptimize(encoded);
        benchmark::ClobberMemory();
    }
}

void Certificate_OpenSSL_Decode(benchmark::State& state) {
    for (auto _ : state) {
        auto value = asn1kit::benchmarks::openssl_support::decode_certificate(g_cert_der);
        benchmark::DoNotOptimize(value.get());
        benchmark::ClobberMemory();
    }
}

void Certificate_OpenSSL_Encode(benchmark::State& state) {
    for (auto _ : state) {
        auto encoded =
            asn1kit::benchmarks::openssl_support::encode_certificate(*g_openssl_certificate);
        benchmark::DoNotOptimize(encoded.data());
        benchmark::DoNotOptimize(encoded.size());
        benchmark::ClobberMemory();
    }
}

} // namespace

BENCHMARK(Certificate_Asn1Kit_Decode)->Unit(benchmark::kMicrosecond)->Setup(SetupCertificate);
BENCHMARK(Certificate_Asn1Kit_Encode)->Unit(benchmark::kMicrosecond)->Setup(SetupCertificate);
BENCHMARK(Certificate_OpenSSL_Decode)->Unit(benchmark::kMicrosecond)->Setup(SetupCertificate);
BENCHMARK(Certificate_OpenSSL_Encode)->Unit(benchmark::kMicrosecond)->Setup(SetupCertificate);
