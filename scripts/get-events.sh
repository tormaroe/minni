#!/usr/bin/env bash
set -euo pipefail

# Configuration
PORT=${MINNI_PORT:-25000}
AGGREGATE_ID=${1:-sample-aggregate}

echo "Fetching events for stream '$AGGREGATE_ID' from http://localhost:$PORT..."
RESPONSE=$(curl -s "http://localhost:$PORT/streams/$AGGREGATE_ID")

echo "Raw Response:"
echo "$RESPONSE"
echo ""
echo "Decoded Event Payloads:"

# Use python to decode base64 payloads and format JSON outputs
if command -v python3 &>/dev/null; then
    PYTHON_CMD="python3"
elif command -v python &>/dev/null; then
    PYTHON_CMD="python"
else
    PYTHON_CMD=""
fi

if [ -n "$PYTHON_CMD" ]; then
    "$PYTHON_CMD" -c "
import sys, json, base64
try:
    data = json.loads(sys.argv[1])
    if not isinstance(data, list):
        print('Unexpected response shape:', data)
        sys.exit(0)
    for item in data:
        seq = item.get('sequenceNumber')
        ts = item.get('timestamp')
        encoded_payload = item.get('data', '')
        try:
            decoded = base64.b64decode(encoded_payload).decode('utf-8')
            try:
                # Try to pretty print if the payload is JSON
                parsed = json.loads(decoded)
                decoded = json.dumps(parsed, indent=2)
            except:
                pass
        except Exception as e:
            decoded = f'<Decode Error: {e}>'
        print(f'=== Event #{seq} | Timestamp: {ts} ===')
        print(decoded)
        print()
except Exception as e:
    print('Failed to parse response JSON:', e)
" "$RESPONSE"
else
    echo "Python was not found, unable to automatically decode base64 payloads."
fi
