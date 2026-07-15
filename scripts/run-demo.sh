#!/usr/bin/env bash
set -euo pipefail
base="${VSMC_ENDPOINT:-http://localhost:8080}"
post(){ curl -fsS -X POST "$base/api/v1/commands" -H 'content-type: application/json' -d "$1"; echo; }
post '{"type":"initialize","requestedBy":"demo-script"}'
sleep .2
post '{"type":"home","requestedBy":"demo-script"}'
sleep 3
post '{"type":"load-order","orderId":"DEMO-ORDER-001","quantity":3,"recipeId":"standard-widget","recipeRevision":1,"requestedBy":"demo-script"}'
post '{"type":"set-mode","mode":"Automatic","requestedBy":"demo-script"}'
post '{"type":"start","requestedBy":"demo-script"}'
