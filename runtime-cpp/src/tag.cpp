// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "asn1kit/tag.hpp"

namespace asn1kit {

const tag tag::boolean{tag_class::universal, 1};
const tag tag::integer{tag_class::universal, 2};
const tag tag::bit_string{tag_class::universal, 3};
const tag tag::octet_string{tag_class::universal, 4};
const tag tag::null{tag_class::universal, 5};
const tag tag::object_identifier{tag_class::universal, 6};
const tag tag::enumerated{tag_class::universal, 10};
const tag tag::utf8_string{tag_class::universal, 12};
const tag tag::sequence{tag_class::universal, 16, true};
const tag tag::set{tag_class::universal, 17, true};
const tag tag::numeric_string{tag_class::universal, 18};
const tag tag::printable_string{tag_class::universal, 19};
const tag tag::teletex_string{tag_class::universal, 20};
const tag tag::videotex_string{tag_class::universal, 21};
const tag tag::ia5_string{tag_class::universal, 22};
const tag tag::utc_time{tag_class::universal, 23};
const tag tag::generalized_time{tag_class::universal, 24};
const tag tag::graphic_string{tag_class::universal, 25};
const tag tag::visible_string{tag_class::universal, 26};
const tag tag::general_string{tag_class::universal, 27};
const tag tag::universal_string{tag_class::universal, 28};
const tag tag::bmp_string{tag_class::universal, 30};

std::string tag::to_string() const {
    const char* cls = "Universal";
    switch (class_) {
    case tag_class::universal:
        cls = "Universal";
        break;
    case tag_class::application:
        cls = "Application";
        break;
    case tag_class::context_specific:
        cls = "ContextSpecific";
        break;
    case tag_class::private_:
        cls = "Private";
        break;
    }
    return std::string(cls) + "-" + std::to_string(number_) + (constructed_ ? "C" : "P");
}

} // namespace asn1kit
