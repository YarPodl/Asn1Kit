// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#pragma once

#include "asn1kit/exception.hpp"

#include <cstdint>
#include <string>

namespace asn1kit {

enum class tag_class : std::uint8_t {
    universal = 0,
    application = 1,
    context_specific = 2,
    private_ = 3
};

enum class string_form {
    utf8,
    printable,
    teletex,
    t61,
    ia5,
    numeric,
    visible,
    bmp,
    universal,
    general,
    graphic,
    videotex
};

enum class time_form {
    utc,
    generalized
};

/// ASN.1 tag: class, number, and primitive/constructed form.
class tag {
public:
    tag() = default;

    tag(tag_class cls, int number, bool constructed = false)
        : class_(cls), number_(number), constructed_(constructed) {
        if (number < 0) {
            throw exception("Tag number must be non-negative.");
        }
    }

    [[nodiscard]] tag_class tag_class_value() const noexcept { return class_; }
    [[nodiscard]] int number() const noexcept { return number_; }
    [[nodiscard]] bool constructed() const noexcept { return constructed_; }

    [[nodiscard]] tag as_constructed() const { return tag(class_, number_, true); }
    [[nodiscard]] tag as_primitive() const { return tag(class_, number_, false); }

    [[nodiscard]] bool matches_ignore_constructed(const tag& other) const noexcept {
        return class_ == other.class_ && number_ == other.number_;
    }

    [[nodiscard]] std::string to_string() const;

    static const tag boolean;
    static const tag integer;
    static const tag bit_string;
    static const tag octet_string;
    static const tag null;
    static const tag object_identifier;
    static const tag enumerated;
    static const tag utf8_string;
    static const tag sequence;
    static const tag set;
    static const tag numeric_string;
    static const tag printable_string;
    static const tag teletex_string;
    static const tag videotex_string;
    static const tag ia5_string;
    static const tag utc_time;
    static const tag generalized_time;
    static const tag graphic_string;
    static const tag visible_string;
    static const tag general_string;
    static const tag universal_string;
    static const tag bmp_string;

    friend bool operator==(const tag& a, const tag& b) noexcept {
        return a.class_ == b.class_ && a.number_ == b.number_ && a.constructed_ == b.constructed_;
    }
    friend bool operator!=(const tag& a, const tag& b) noexcept { return !(a == b); }

private:
    tag_class class_{tag_class::universal};
    int number_{0};
    bool constructed_{false};
};

} // namespace asn1kit
