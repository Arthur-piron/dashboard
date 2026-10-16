#!/usr/bin/env bash
# Walks the GitHub OAuth flow end to end, because /authorize needs a JWT that a browser
# navigation cannot carry: creates a throwaway confirmed user, asks the API for the authorize
# URL, opens it, then waits for the subscription to show up.
#
#   ./bonus/test-github-oauth.sh                          # host API (dotnet run)
#   API=http://localhost:8081/api ./bonus/test-github-oauth.sh   # the Dockerised stack
#   OPEN=0 ./bonus/test-github-oauth.sh                   # print the URL instead of opening it

set -euo pipefail

API=${API:-http://localhost:5119}
MAILPIT=${MAILPIT:-http://localhost:8025}
OPEN=${OPEN:-1}

stamp=$(date +%s)
email="github-test-$stamp@example.com"
password="Password123"

curl -fsS "$API/about.json" >/dev/null 2>&1 || {
  echo "No API on $API. Start it with: dotnet run --project DashboardBackEnd" >&2
  exit 1
}

echo "==> registering $email"
curl -fsS -X POST "$API/auth/register" -H 'Content-Type: application/json' \
  -d "{\"email\":\"$email\",\"username\":\"gh$stamp\",\"password\":\"$password\"}" >/dev/null

echo "==> confirming it from Mailpit"
for _ in $(seq 1 40); do
  id=$(curl -fsS "$MAILPIT/api/v1/search?query=to:$email" | jq -r '.messages[0].ID // empty')
  [ -n "$id" ] && break
  sleep 0.5
done
[ -n "${id:-}" ] || { echo "No confirmation email arrived. Is Mailpit up on $MAILPIT?" >&2; exit 1; }

link=$(curl -fsS "$MAILPIT/api/v1/message/$id" | jq -r '.HTML' | grep -o 'href="[^"]*"' | head -1 | cut -d'"' -f2)
curl -fsS "$API/auth/confirm?${link#*\?}" >/dev/null

echo "==> logging in"
token=$(curl -fsS -X POST "$API/auth/login" -H 'Content-Type: application/json' \
  -d "{\"email\":\"$email\",\"password\":\"$password\"}" | jq -r '.token')

echo "==> asking for the authorize URL"
response=$(curl -fsS -w '\n%{http_code}' "$API/services/github/oauth/authorize" \
  -H "Authorization: Bearer $token") || true
status=$(tail -1 <<<"$response")
body=$(sed '$d' <<<"$response")

if [ "$status" != "200" ]; then
  echo "HTTP $status: $body" >&2
  [ "$status" = "503" ] && echo "Set GitHub:ClientId and GitHub:ClientSecret (user-secrets for dotnet run, .env for compose)." >&2
  exit 1
fi

url=$(jq -r '.url' <<<"$body")
echo
echo "$url"
echo
[ "$OPEN" = "1" ] && command -v xdg-open >/dev/null && xdg-open "$url" >/dev/null 2>&1 &

echo "==> authorize it in the browser; waiting up to 120s for the link (state expires in 10 min)"
for _ in $(seq 1 120); do
  if curl -fsS "$API/subscriptions" -H "Authorization: Bearer $token" | jq -e '.[] | select(.serviceKey=="github")' >/dev/null 2>&1; then
    echo
    echo "linked. the account is $email / $password"
    curl -fsS "$API/subscriptions" -H "Authorization: Bearer $token" | jq
    exit 0
  fi
  sleep 1
done

echo "no subscription showed up. Check the API log for a 'github=failed' redirect." >&2
exit 1
