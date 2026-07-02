#!/usr/bin/env bash
set -euo pipefail

# Configuration
PORT=${MINNI_PORT:-25000}
AGGREGATE_ID=${1:-sample-aggregate}
PAYLOAD=${2:-'{"message": "Hello MinniStore!"}'}

# base64 encode the JSON payload (tr deletes line endings if base64 adds them)
BASE64_DATA=$(echo -n "$PAYLOAD" | base64 | tr -d '\r\n')

REQUEST_BODY=$(cat <<EOF
{
  "events": [
    {
      "data": "$BASE64_DATA"
    }
  ]
}
EOF
)

echo "Posting event to stream '$AGGREGATE_ID' on http://localhost:$PORT..."
curl -i -X POST \
     -H "Content-Type: application/json" \
     -d "$REQUEST_BODY" \
     "http://localhost:$PORT/streams/$AGGREGATE_ID"
