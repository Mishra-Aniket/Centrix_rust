#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${1:-https://localhost:5201}"
HEALTH_URL="${BASE_URL}/health"

response="$(curl --fail --silent --show-error --insecure "${HEALTH_URL}")"
printf '%s\n' "${response}"
printf '%s' "${response}" | grep -q '"status":"Healthy"'
