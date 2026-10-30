// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "fixture_loader.hpp"
#include "openssl_support.hpp"

#include "CryptographicMessageSyntax2009.hpp"

#include "asn1kit/encode_decode.hpp"
#include "asn1kit/writer.hpp"

#include <benchmark/benchmark.h>

#include <cstdint>
#include <stdexcept>
#include <vector>

namespace {

namespace Cms = asn1kit::modern::CryptographicMessageSyntax2009;

std::vector<std::uint8_t> g_cms_der;
Cms::ContentInfo g_content_info{};
Cms::SignedData g_signed_data{};
asn1kit::writer g_encode_writer{asn1kit::encoding::der};
asn1kit::benchmarks::openssl_support::cms_content_info_ptr g_openssl_content_info;

[[nodiscard]] Cms::SignedData decode_signed_data(const std::vector<std::uint8_t>& der) {
    auto info = asn1kit::decode<Cms::ContentInfo>(der, asn1kit::encoding::der);
    auto signed_data = info.content_set().signed_data();
    if (!signed_data) {
        throw std::runtime_error("Expected SignedData content.");
    }
    return std::move(*signed_data);
}

void materialize_signed_data(Cms::SignedData& signed_data) {
    if (!signed_data.certificates.has_value()) {
        return;
    }
    for (auto& choice : *signed_data.certificates) {
        if (choice.kind != Cms::CertificateChoices_kind::certificate) {
            continue;
        }
        auto& cert = std::get<0>(choice.value);
        // Non-const get() invalidates retained TBS TLV (full structural path on later encode).
        auto& tbs = cert.toBeSigned.get();
        benchmark::DoNotOptimize(tbs.serialNumber);
        benchmark::DoNotOptimize(tbs.issuer.get());
        benchmark::DoNotOptimize(tbs.subject.get());
        benchmark::DoNotOptimize(tbs.subjectPublicKeyInfo.get());
    }
}

void SetupCms(const benchmark::State&) {
    g_cms_der = asn1kit::benchmarks::read_cms_fixture("attached-signeddata.p7m");
    g_content_info = asn1kit::decode<Cms::ContentInfo>(g_cms_der, asn1kit::encoding::der);
    auto signed_data = g_content_info.content_set().signed_data();
    if (!signed_data) {
        throw std::runtime_error("CMS setup expected SignedData content.");
    }
    g_signed_data = std::move(*signed_data);
    g_encode_writer.ensure_capacity(g_cms_der.size() + 256);
    g_openssl_content_info = asn1kit::benchmarks::openssl_support::decode_cms(g_cms_der);
}

void Cms_Asn1Kit_Lazy_Decode(benchmark::State& state) {
    for (auto _ : state) {
        auto signed_data = decode_signed_data(g_cms_der);
        benchmark::DoNotOptimize(signed_data);
        benchmark::ClobberMemory();
    }
}

void Cms_Asn1Kit_Lazy_Materialize_Decode(benchmark::State& state) {
    for (auto _ : state) {
        auto info = asn1kit::decode<Cms::ContentInfo>(g_cms_der, asn1kit::encoding::der);
        auto signed_data = info.content_set().signed_data();
        if (!signed_data) {
            throw std::runtime_error("Expected SignedData content.");
        }
        materialize_signed_data(*signed_data);
        benchmark::DoNotOptimize(info);
        benchmark::DoNotOptimize(signed_data);
        benchmark::ClobberMemory();
    }
}

void Cms_Asn1Kit_Lazy_Encode(benchmark::State& state) {
    for (auto _ : state) {
        // Re-bind SignedData each op so Encode pays structural cost (not opaque WriteAny of retained content).
        g_encode_writer.reset();
        g_content_info.content_set().signed_data(g_signed_data);
        g_content_info.encode(g_encode_writer);
        auto encoded = g_encode_writer.encode();
        benchmark::DoNotOptimize(encoded);
        benchmark::ClobberMemory();
    }
}

void Cms_OpenSSL_Decode(benchmark::State& state) {
    for (auto _ : state) {
        auto value = asn1kit::benchmarks::openssl_support::decode_cms(g_cms_der);
        benchmark::DoNotOptimize(value.get());
        benchmark::ClobberMemory();
    }
}

void Cms_OpenSSL_Encode(benchmark::State& state) {
    for (auto _ : state) {
        auto encoded =
            asn1kit::benchmarks::openssl_support::encode_cms(*g_openssl_content_info);
        benchmark::DoNotOptimize(encoded.data());
        benchmark::DoNotOptimize(encoded.size());
        benchmark::ClobberMemory();
    }
}

} // namespace

BENCHMARK(Cms_Asn1Kit_Lazy_Decode)->Unit(benchmark::kMicrosecond)->Setup(SetupCms);
BENCHMARK(Cms_Asn1Kit_Lazy_Materialize_Decode)
    ->Unit(benchmark::kMicrosecond)
    ->Setup(SetupCms);
BENCHMARK(Cms_Asn1Kit_Lazy_Encode)->Unit(benchmark::kMicrosecond)->Setup(SetupCms);
BENCHMARK(Cms_OpenSSL_Decode)->Unit(benchmark::kMicrosecond)->Setup(SetupCms);
BENCHMARK(Cms_OpenSSL_Encode)->Unit(benchmark::kMicrosecond)->Setup(SetupCms);
