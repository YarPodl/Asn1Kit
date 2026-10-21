# Copyright Asn1Kit contributors
# SPDX-License-Identifier: Apache-2.0
#
# Usage:
#   cmake -DDEMO=... -DEE=... -DISSUER=... -DOUT_DIR=... -P ocsp_roundtrip_smoke.cmake

if(NOT DEFINED DEMO OR NOT DEFINED EE OR NOT DEFINED ISSUER OR NOT DEFINED OUT_DIR)
    message(FATAL_ERROR "DEMO, EE, ISSUER, and OUT_DIR must be set.")
endif()

file(MAKE_DIRECTORY "${OUT_DIR}")
set(REQ "${OUT_DIR}/request.der")
set(RESP "${OUT_DIR}/response.der")

execute_process(
    COMMAND "${DEMO}" ocsp-request --certificate "${EE}" --issuer "${ISSUER}" --output "${REQ}"
    RESULT_VARIABLE _req_rc
    OUTPUT_VARIABLE _req_out
    ERROR_VARIABLE _req_err
)
if(NOT _req_rc EQUAL 0)
    message(FATAL_ERROR "ocsp-request failed (${_req_rc}): ${_req_err}${_req_out}")
endif()

execute_process(
    COMMAND "${DEMO}" ocsp-respond --request "${REQ}" --output "${RESP}"
    RESULT_VARIABLE _resp_rc
    OUTPUT_VARIABLE _resp_out
    ERROR_VARIABLE _resp_err
)
if(NOT _resp_rc EQUAL 0)
    message(FATAL_ERROR "ocsp-respond failed (${_resp_rc}): ${_resp_err}${_resp_out}")
endif()

execute_process(
    COMMAND "${DEMO}" ocsp-check --request "${REQ}" --response "${RESP}"
    RESULT_VARIABLE _check_rc
    OUTPUT_VARIABLE _check_out
    ERROR_VARIABLE _check_err
)
if(NOT _check_rc EQUAL 0)
    message(FATAL_ERROR "ocsp-check failed (${_check_rc}): ${_check_err}${_check_out}")
endif()
