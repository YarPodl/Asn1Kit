# Copyright Asn1Kit contributors
# SPDX-License-Identifier: Apache-2.0

from conan import ConanFile
from conan.tools.cmake import CMakeDeps, CMakeToolchain


class Asn1kitRuntimeConan(ConanFile):
    """Consumer recipe for CMakeToolchain/CMakeDeps.

    Test dependencies stay in the host context and are not propagated to consumers.
    """

    settings = "os", "compiler", "build_type", "arch"
    options = {
        "build_tests": [True, False],
        "build_benchmarks": [True, False],
    }
    default_options = {
        "build_tests": True,
        "build_benchmarks": False,
        "openssl/*:shared": False,
    }

    def build_requirements(self):
        if self.options.build_tests:
            self.test_requires("gtest/1.15.0")
            self.test_requires("nlohmann_json/3.11.3")
        if self.options.build_benchmarks:
            self.test_requires("benchmark/1.9.1")
            self.test_requires("openssl/3.5.7")

    def generate(self):
        deps = CMakeDeps(self)
        deps.generate()
        tc = CMakeToolchain(self)
        tc.user_presets_path = False
        tc.variables["CMAKE_CXX_STANDARD"] = "20"
        tc.variables["ASN1KIT_BUILD_TESTS"] = bool(self.options.build_tests)
        tc.variables["ASN1KIT_BUILD_BENCHMARKS"] = bool(self.options.build_benchmarks)
        tc.generate()
