# Copyright Asn1Kit contributors
# SPDX-License-Identifier: Apache-2.0

from conan import ConanFile
from conan.tools.cmake import CMakeDeps, CMakeToolchain


class Asn1kitRuntimeConan(ConanFile):
    """Consumer recipe for CMakeToolchain/CMakeDeps.

    gtest and nlohmann_json are declared here so `conan install` can resolve them,
    but CMake links them only to the test target (never to libasn1kit).
    """

    settings = "os", "compiler", "build_type", "arch"

    def requirements(self):
        # Test-only consumers (see tests/CMakeLists.txt). Not linked into asn1kit.
        # Declared as requires so `conan install` exposes them to CMake; linked only in tests/.
        self.requires("gtest/1.15.0")
        self.requires("nlohmann_json/3.11.3")

    def generate(self):
        deps = CMakeDeps(self)
        deps.generate()
        tc = CMakeToolchain(self)
        tc.user_presets_path = False
        tc.variables["CMAKE_CXX_STANDARD"] = "20"
        tc.generate()
