// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "smoke.hpp"

#include <benchmark/benchmark.h>
#include <openssl/crypto.h>

#include <cstdio>
#include <cstdlib>
#include <exception>

int main(int argc, char** argv) {
    try {
        asn1kit::benchmarks::verify_fixtures();
    } catch (const std::exception& ex) {
        std::fprintf(stderr, "asn1kit benchmarks smoke failed: %s\n", ex.what());
        return EXIT_FAILURE;
    }

    benchmark::AddCustomContext("openssl_version", OpenSSL_version(OPENSSL_VERSION));
    benchmark::Initialize(&argc, argv);
    if (benchmark::ReportUnrecognizedArguments(argc, argv)) {
        return EXIT_FAILURE;
    }
    benchmark::RunSpecifiedBenchmarks();
    benchmark::Shutdown();
    return EXIT_SUCCESS;
}
