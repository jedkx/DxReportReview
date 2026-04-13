#!/bin/sh
# Replaces PLACEHOLDER_KEY in nuget.config. Argument 1 = raw key or full feed URL from user.
# Runs inside Docker build only (no secrets in repo).
set -e
CONFIG="${2:-/src/nuget.config}"
RAW=$(printf '%s' "$1" | tr -d '\r\n')

if [ -z "$RAW" ]; then
  echo "ERROR: DEVEXPRESS_NUGET_KEY is empty. Set it in .env (see README)."
  exit 1
fi

# Common mistakes: documentation placeholders
case "$RAW" in
  *'{KEY}'*|*'{key}'*|*'<YOUR'*|*'<your'*|*'YOUR_KEY'*|*'your_trial_key_here'*|*'your_trial_key_or_full_feed_url_here'*)
    echo "ERROR: DEVEXPRESS_NUGET_KEY still contains a placeholder (e.g. {KEY})."
    echo "Open https://nuget.devexpress.com and copy your personal feed URL or the token segment only."
    exit 1
    ;;
esac

TOKEN="$RAW"
# Full URL from DevExpress site
case "$TOKEN" in
  https://nuget.devexpress.com/*) TOKEN="${TOKEN#https://nuget.devexpress.com/}";;
  http://nuget.devexpress.com/*) TOKEN="${TOKEN#http://nuget.devexpress.com/}";;
esac
TOKEN="${TOKEN%%/api*}"
TOKEN="${TOKEN%/}"

if [ -z "$TOKEN" ]; then
  echo "ERROR: Could not parse a NuGet token from DEVEXPRESS_NUGET_KEY."
  exit 1
fi

case "$TOKEN" in
  *'://'*)
    echo "ERROR: Token still looks like a URL. Paste only the token or the official https://nuget.devexpress.com/<token>/api URL."
    exit 1
    ;;
esac

sed -i "s|PLACEHOLDER_KEY|${TOKEN}|g" "$CONFIG"
echo "DevExpress NuGet source configured (token length: ${#TOKEN})."
