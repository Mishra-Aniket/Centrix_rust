#!/usr/bin/env bash
set -euo pipefail

curl --fail --silent --show-error --insecure https://localhost:5201/api/monitor/snapshot
printf '\n'
