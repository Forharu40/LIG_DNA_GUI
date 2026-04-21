#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

IMAGE_NAME="${IMAGE_NAME:-meva-yolo-demo}"
GUI_HOST="${GUI_HOST:-192.168.1.94}"
GUI_PORT="${GUI_PORT:-5000}"
SOURCE_ROOT="${SOURCE_ROOT:-/data/MEVA}"
CLIP_START_SECONDS="${CLIP_START_SECONDS:-0}"
CLIP_DURATION_SECONDS="${CLIP_DURATION_SECONDS:-10}"
SAMPLE_INTERVAL_SECONDS="${SAMPLE_INTERVAL_SECONDS:-3600}"
SAMPLE_START_RATIO="${SAMPLE_START_RATIO:-0.8}"
CONFIDENCE="${CONFIDENCE:-0.60}"
DETECTION_INTERVAL_FRAMES="${DETECTION_INTERVAL_FRAMES:-4}"
JPEG_QUALITY="${JPEG_QUALITY:-60}"
MAX_UDP_BYTES="${MAX_UDP_BYTES:-60000}"
HOST_MEVA_PATH="${HOST_MEVA_PATH:-$HOME/datashets/MEVA}"

BUILD_IMAGE=0

print_usage() {
  cat <<'EOF'
Usage:
  bash ./run_meva_yolo_demo.sh [--build]

Options:
  --build    Force rebuild the Docker image before running.

Environment overrides:
  GUI_HOST, GUI_PORT, SOURCE_ROOT, CLIP_START_SECONDS, CLIP_DURATION_SECONDS
  SAMPLE_INTERVAL_SECONDS, SAMPLE_START_RATIO, CONFIDENCE
  DETECTION_INTERVAL_FRAMES, JPEG_QUALITY, MAX_UDP_BYTES
  HOST_MEVA_PATH, IMAGE_NAME
EOF
}

for arg in "$@"; do
  case "$arg" in
    --build)
      BUILD_IMAGE=1
      ;;
    --help|-h)
      print_usage
      exit 0
      ;;
    *)
      echo "Unknown option: $arg" >&2
      print_usage
      exit 1
      ;;
  esac
done

if [[ ! -d "$HOST_MEVA_PATH" ]]; then
  echo "MEVA source directory not found: $HOST_MEVA_PATH" >&2
  exit 1
fi

if ! sudo docker image inspect "$IMAGE_NAME" >/dev/null 2>&1; then
  BUILD_IMAGE=1
fi

if [[ "$BUILD_IMAGE" -eq 1 ]]; then
  echo "Building Docker image: $IMAGE_NAME"
  sudo docker build -t "$IMAGE_NAME" "$SCRIPT_DIR"
fi

echo "Running Docker image: $IMAGE_NAME"
echo "GUI_HOST=$GUI_HOST GUI_PORT=$GUI_PORT"
echo "HOST_MEVA_PATH=$HOST_MEVA_PATH"

sudo docker run --rm \
  --runtime nvidia \
  --network host \
  -e GUI_HOST="$GUI_HOST" \
  -e GUI_PORT="$GUI_PORT" \
  -e SOURCE_ROOT="$SOURCE_ROOT" \
  -e CLIP_START_SECONDS="$CLIP_START_SECONDS" \
  -e CLIP_DURATION_SECONDS="$CLIP_DURATION_SECONDS" \
  -e SAMPLE_INTERVAL_SECONDS="$SAMPLE_INTERVAL_SECONDS" \
  -e SAMPLE_START_RATIO="$SAMPLE_START_RATIO" \
  -e CONFIDENCE="$CONFIDENCE" \
  -e DETECTION_INTERVAL_FRAMES="$DETECTION_INTERVAL_FRAMES" \
  -e JPEG_QUALITY="$JPEG_QUALITY" \
  -e MAX_UDP_BYTES="$MAX_UDP_BYTES" \
  -v "$HOST_MEVA_PATH:$SOURCE_ROOT:ro" \
  "$IMAGE_NAME"
