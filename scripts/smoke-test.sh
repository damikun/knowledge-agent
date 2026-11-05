#!/usr/bin/env bash
# End-to-end smoke test against a running stack: API health, add a text source and wait until it is ingested,
# semantic search, one chat turn (SSE), the portal (and its /api proxy) and, optionally, Langfuse.
#
#   scripts/smoke-test.sh                                  # docker compose defaults
#   PORTAL_URL=http://localhost:5173 scripts/smoke-test.sh # Aspire (portal = Vite dev server)
#   PORTAL_URL= LANGFUSE_URL= scripts/smoke-test.sh        # API only (e.g. the in-memory launch profile)
#
# Requires curl and jq. Exits non-zero on the first failed check.
set -euo pipefail

API_URL="${API_URL:-http://localhost:5080}"
PORTAL_URL="${PORTAL_URL-http://localhost:8080}"
LANGFUSE_URL="${LANGFUSE_URL-http://localhost:3000}"
LANGFUSE_PUBLIC_KEY="${LANGFUSE_PUBLIC_KEY:-pk-lf-local-dev}"
LANGFUSE_SECRET_KEY="${LANGFUSE_SECRET_KEY:-sk-lf-local-dev}"
TIMEOUT_SECONDS="${TIMEOUT_SECONDS:-180}"

for tool in curl jq; do
  command -v "$tool" >/dev/null || { echo "smoke-test: '$tool' is required" >&2; exit 2; }
done

pass() { printf '  \033[32mok\033[0m   %s\n' "$*"; }
fail() { printf '  \033[31mFAIL\033[0m %s\n' "$*" >&2; exit 1; }
deadline=$((SECONDS + TIMEOUT_SECONDS))

echo "API $API_URL"
until [ "$(curl -s --max-time 5 "$API_URL/health" || true)" = "Healthy" ]; do
  [ $SECONDS -lt $deadline ] || fail "GET /health did not report Healthy within ${TIMEOUT_SECONDS}s"
  sleep 2
done
pass "GET /health -> Healthy"

info=$(curl -sf "$API_URL/api/system/info") || fail "GET /api/system/info"
pass "GET /api/system/info -> $(jq -c '{persistenceProvider, vectorStoreProvider, promptStore, langfuseConfigured, configuredProviders: [.providers[]? | select(.configured == true) | .key]}' <<<"$info")"

# Ingestion, search and chat need real models; say so instead of failing halfway.
profiles=$(curl -sf "$API_URL/api/models/profiles") || fail "GET /api/models/profiles"
for profile in orchestrator embedding; do
  model=$(jq -r --arg p "$profile" '.[] | select(.name == $p) | if .effective then "\(.effective.providerKey)/\(.effective.model)" else "" end' <<<"$profiles")
  [ -n "$model" ] || fail "no model for the '$profile' profile: connect a chat and an embedding model on the Models page or in .env"
  pass "$profile profile -> $model"
done

marker="smoke-$(date +%s)-$RANDOM"
body=$(jq -n --arg marker "$marker" '{
  title: "Smoke test \($marker)",
  format: "markdown",
  content: "# Smoke test\n\nThe Knowledge Agent smoke test marker is \($marker). Aurora borealis lights are caused by charged solar particles colliding with gases in the upper atmosphere."
}')
created=$(curl -sf -X POST "$API_URL/api/sources/text" -H 'Content-Type: application/json' -d "$body") \
  || fail "POST /api/sources/text"
source_id=$(jq -r '.id' <<<"$created")
pass "POST /api/sources/text -> $source_id ($(jq -r '.status' <<<"$created"))"

while :; do
  source=$(curl -sf "$API_URL/api/sources/$source_id") || fail "GET /api/sources/$source_id"
  status=$(jq -r '.status' <<<"$source")
  case "$status" in
    ready) break ;;
    failed) fail "source ingestion failed: $(jq -r '.error' <<<"$source")" ;;
    waiting) fail "source waits for an embedding model: $(jq -r '.error' <<<"$source")" ;;
  esac
  [ $SECONDS -lt $deadline ] || fail "source still '$status' after ${TIMEOUT_SECONDS}s"
  sleep 1
done
pass "source ready: $(jq -c '{chunkCount, embeddingModel}' <<<"$source")"

hits=$(curl -sf -X POST "$API_URL/api/search" -H 'Content-Type: application/json' \
  -d "$(jq -n --arg q "What causes aurora borealis lights? $marker" '{query: $q, topK: 3}')") || fail "POST /api/search"
jq -e --arg id "$source_id" '[.. | objects | select(.sourceId? == $id)] | length > 0' <<<"$hits" >/dev/null \
  || fail "POST /api/search did not return the new source: $hits"
pass "POST /api/search -> hit from the new source"

chat=$(curl -sN --max-time 120 -X POST "$API_URL/api/chat" -H 'Content-Type: application/json' -H 'Accept: text/event-stream' \
  -d "$(jq -n --arg m "What causes aurora borealis lights?" --arg id "$source_id" '{message: $m, sourceIds: [$id]}')") \
  || fail "POST /api/chat"
grep -q '^event: done' <<<"$chat" || fail "POST /api/chat: no 'done' event. Stream: $(head -c 600 <<<"$chat")"
grep -q '^event: error' <<<"$chat" && fail "POST /api/chat streamed an error: $(grep -A1 '^event: error' <<<"$chat" | head -2)"
pass "POST /api/chat -> $(grep -c '^event: ' <<<"$chat") SSE events, ends with 'done'"

if [ -n "$PORTAL_URL" ]; then
  echo "Portal $PORTAL_URL"
  page=$(curl -sf "$PORTAL_URL/") || fail "GET $PORTAL_URL/"
  grep -qi '<div id="root"' <<<"$page" || fail "portal index.html has no #root element"
  pass "GET / -> SPA index.html"
  curl -sf "$PORTAL_URL/api/system/info" >/dev/null || fail "GET $PORTAL_URL/api/system/info (API proxy)"
  pass "GET /api/system/info through the portal proxy"
fi

if [ -n "$LANGFUSE_URL" ]; then
  echo "Langfuse $LANGFUSE_URL"
  health=$(curl -sf "$LANGFUSE_URL/api/public/health") || fail "GET /api/public/health"
  pass "GET /api/public/health -> $health"
  # The API seeds its default prompts into Langfuse in the background shortly after startup.
  until prompts=$(curl -sf -u "$LANGFUSE_PUBLIC_KEY:$LANGFUSE_SECRET_KEY" "$LANGFUSE_URL/api/public/v2/prompts?limit=50") \
      && jq -e '[.data[].name] | index("orchestrator-system") != null' <<<"$prompts" >/dev/null; do
    [ $SECONDS -lt $deadline ] || fail "prompt 'orchestrator-system' not found in Langfuse (seeding): ${prompts:-no response}"
    sleep 3
  done
  pass "Langfuse prompts: $(jq -c '[.data[].name]' <<<"$prompts")"
fi

echo "Smoke test passed."
