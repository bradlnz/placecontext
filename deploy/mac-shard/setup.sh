#!/usr/bin/env bash
# One-shot setup: install deps, download model, start shard server.
# Run on each node in the cluster.
#
# Usage:
#   bash setup.sh                          # full model on this node
#   bash setup.sh --shard 0/2              # first of two shards
#   bash setup.sh --port 8081 --shard 1/2  # second shard on different port

set -euo pipefail

PORT="${PORT:-8080}"
MODEL="${MODEL_PATH:-Qwen/Qwen3.5-4B}"
SHARD=""
VENV_DIR="$HOME/.venv/shard-server"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --port)  PORT="$2"; shift 2 ;;
    --shard) SHARD="$2"; shift 2 ;;
    --model) MODEL="$2"; shift 2 ;;
    *) echo "Unknown arg: $1"; exit 1 ;;
  esac
done

echo "=== Qwen Shard Server Setup ==="
echo "Model:  $MODEL"
echo "Port:   $PORT"
echo "Shard:  ${SHARD:-full model}"
echo ""

# 1. Create venv
if [ ! -d "$VENV_DIR" ]; then
  echo "--- Creating venv at $VENV_DIR ---"
  python3 -m venv "$VENV_DIR"
fi
# shellcheck disable=SC1091
source "$VENV_DIR/bin/activate"

# 2. Install deps
echo "--- Installing Python packages ---"
pip install --upgrade pip -q
pip install torch transformers safetensors fastapi uvicorn pyyaml -q

# 3. Pre-download model
echo "--- Downloading model: $MODEL ---"
python3 -c "from huggingface_hub import snapshot_download; snapshot_download('$MODEL', ignore_patterns=['*.bin'])" 2>/dev/null || \
  pip install huggingface_hub -q && \
  python3 -c "from huggingface_hub import snapshot_download; snapshot_download('$MODEL', ignore_patterns=['*.bin'])"

# 4. Print model directory
MODEL_DIR=$(python3 -c "from huggingface_hub import try_to_load_from_cache; import os; print(os.path.dirname(try_to_load_from_cache('$MODEL', 'config.json')))" 2>/dev/null || echo "$HOME/.cache/huggingface/hub/models--Qwen--Qwen3-3B")
echo "Model cached at: $MODEL_DIR"

# 5. Start server
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
echo ""
echo "--- Starting shard server on port $PORT ---"
ARGS="--model $MODEL --port $PORT"
if [ -n "$SHARD" ]; then
  ARGS="$ARGS --shard $SHARD"
fi

exec python3 "$SCRIPT_DIR/server.py" $ARGS
