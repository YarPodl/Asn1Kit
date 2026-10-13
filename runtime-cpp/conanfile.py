# Copyright Asn1Kit contributors
# SPDX-License-Identifier: Apache-2.0

from conan import ConanFile
from conan.tools.cmake import CMakeDeps, CMakeToolchain


class Asn1kitRuntimeConan(ConanFile):
    """Consumer recipe for CMakeToolchain/CMakeDeps.

    Test dependencies stay in the host context and are not propagated to consumers.
    """

    settings = "os", "compiler", "build_type", "arch"
    options = {"build_tests": [True, False]}
    default_options = {"build_tests": True}

    def build_requirements(self):
        if self.options.build_tests:
            self.test_requires("gtest/1.15.0")
            self.test_requires("nlohmann_json/3.11.3")

    def generate(self):
        deps = CMakeDeps(self)
        deps.generate()
        tc = CMakeToolchain(self)
        tc.user_presets_path = False
        tc.variables["CMAKE_CXX_STANDARD"] = "20"
        tc.variables["ASN1KIT_BUILD_TESTS"] = bool(self.options.build_tests)
        tc.generate()
