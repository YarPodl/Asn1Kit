// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "cms_demo.hpp"

#include <fstream>
#include <iostream>
#include <span>
#include <stdexcept>
#include <string>
#include <string_view>
#include <vector>

namespace {

constexpr int exit_ok = 0;
constexpr int exit_asn = 1;
constexpr int exit_usage = 2;

[[nodiscard]] std::vector<std::uint8_t> read_all(const std::string& path) {
    std::ifstream input(path, std::ios::binary);
    if (!input) {
        throw std::runtime_error("Failed to read file: " + path);
    }
    return std::vector<std::uint8_t>(
        (std::istreambuf_iterator<char>(input)), std::istreambuf_iterator<char>());
}

void write_all(const std::string& path, std::span<const std::uint8_t> data) {
    std::ofstream output(path, std::ios::binary);
    if (!output) {
        throw std::runtime_error("Failed to write file: " + path);
    }
    output.write(reinterpret_cast<const char*>(data.data()), static_cast<std::streamsize>(data.size()));
    if (!output) {
        throw std::runtime_error("Failed to write file: " + path);
    }
}

[[nodiscard]] int usage(std::ostream& err) {
    err << "Usage:\n"
        << "  asn1kit_cms_demo verify <path.p7m>\n"
        << "  asn1kit_cms_demo print-cert <path.cer>\n"
        << "  asn1kit_cms_demo print-cms <path.p7m>\n"
        << "  asn1kit_cms_demo sign --certificate <path.cer> --content <path.bin> --output <path.p7m>\n";
    return exit_usage;
}

[[nodiscard]] int run_verify(const std::vector<std::string_view>& args) {
    if (args.size() != 1) {
        return usage(std::cerr);
    }
    try {
        const auto bytes = read_all(std::string(args[0]));
        const auto results = asn1kit::cms_demo::verify_attached(bytes);
        bool all_ok = true;
        for (const auto& result : results) {
            std::cout << "Signer " << result.signer_number << ": "
                      << (result.accepted ? "accepted by educational stub: " : "rejected: ")
                      << result.reason << "\n";
            if (!result.issuer.empty()) {
                std::cout << "  Issuer: " << result.issuer << "\n";
            }
            if (!result.subject.empty()) {
                std::cout << "  Subject: " << result.subject << "\n";
            }
            all_ok = all_ok && result.accepted;
        }
        std::cout << "Educational demonstration only: no cryptographic verification or "
                     "certificate trust validation was performed.\n";
        return all_ok ? exit_ok : exit_asn;
    } catch (const std::exception& ex) {
        std::cerr << ex.what() << "\n";
        return exit_asn;
    }
}

[[nodiscard]] int run_print_cert(const std::vector<std::string_view>& args) {
    if (args.size() != 1) {
        return usage(std::cerr);
    }
    try {
        const auto bytes = read_all(std::string(args[0]));
        asn1kit::cms_demo::print_certificate(bytes, std::cout);
        return exit_ok;
    } catch (const std::exception& ex) {
        std::cerr << ex.what() << "\n";
        return exit_asn;
    }
}

[[nodiscard]] int run_print_cms(const std::vector<std::string_view>& args) {
    if (args.size() != 1) {
        return usage(std::cerr);
    }
    try {
        const auto bytes = read_all(std::string(args[0]));
        asn1kit::cms_demo::print_cms(bytes, std::cout);
        return exit_ok;
    } catch (const std::exception& ex) {
        std::cerr << ex.what() << "\n";
        return exit_asn;
    }
}

[[nodiscard]] int run_sign(const std::vector<std::string_view>& args) {
    std::string certificate;
    std::string content;
    std::string output;
    for (std::size_t i = 0; i < args.size(); ++i) {
        const auto arg = args[i];
        if (arg == "--certificate" && i + 1 < args.size()) {
            certificate = std::string(args[++i]);
        } else if (arg == "--content" && i + 1 < args.size()) {
            content = std::string(args[++i]);
        } else if (arg == "--output" && i + 1 < args.size()) {
            output = std::string(args[++i]);
        } else {
            return usage(std::cerr);
        }
    }
    if (certificate.empty() || content.empty() || output.empty()) {
        return usage(std::cerr);
    }

    try {
        const auto cert_bytes = read_all(certificate);
        const auto content_bytes = read_all(content);
        const auto cms = asn1kit::cms_demo::build_attached_signed_data(content_bytes, cert_bytes);
        write_all(output, cms);

        const auto results = asn1kit::cms_demo::verify_attached(cms);
        std::cout << "Educational CMS SignedData written (placeholder signature; not "
                     "cryptographically valid).\n";
        std::cout << "  Output: " << output << "\n";
        if (!results.empty()) {
            std::cout << "  Issuer: " << results[0].issuer << "\n";
            std::cout << "  Subject: " << results[0].subject << "\n";
        }
        return exit_ok;
    } catch (const std::exception& ex) {
        std::cerr << ex.what() << "\n";
        return exit_asn;
    }
}

} // namespace

int main(int argc, char** argv) {
    if (argc < 2) {
        return usage(std::cerr);
    }
    const std::string_view command = argv[1];
    std::vector<std::string_view> rest;
    rest.reserve(static_cast<std::size_t>(argc > 2 ? argc - 2 : 0));
    for (int i = 2; i < argc; ++i) {
        rest.emplace_back(argv[i]);
    }

    if (command == "verify") {
        return run_verify(rest);
    }
    if (command == "print-cert") {
        return run_print_cert(rest);
    }
    if (command == "print-cms") {
        return run_print_cms(rest);
    }
    if (command == "sign") {
        return run_sign(rest);
    }
    return usage(std::cerr);
}
