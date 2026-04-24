#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

IMAGE_NAME_INPUT="${IMAGE_NAME:-eo-topic-yolo-bridge}"
IMAGE_NAME="${IMAGE_NAME_INPUT,,}"
CONTAINER_NAME="${CONTAINER_NAME:-eo_topic_yolo_bridge}"
BASE_IMAGE="${BASE_IMAGE:-ultralytics/ultralytics:latest-nvidia-arm64}"
MODEL_NAME="${MODEL_NAME:-yolo11s.pt}"

GUI_HOST="${GUI_HOST:-192.168.1.94}"
GUI_PORT="${GUI_PORT:-5000}"
INPUT_IMAGE_TOPIC="${INPUT_IMAGE_TOPIC:-/video/eo/preprocessed}"
OUTPUT_IMAGE_TOPIC="${OUTPUT_IMAGE_TOPIC:-/yolo/eo/image_raw}"
PUBLISH_OUTPUT_TOPIC="${PUBLISH_OUTPUT_TOPIC:-true}"
CONFIDENCE="${CONFIDENCE:-0.60}"
INFERENCE_SIZE="${INFERENCE_SIZE:-640}"
STREAM_MAX_WIDTH="${STREAM_MAX_WIDTH:-854}"
STREAM_MAX_HEIGHT="${STREAM_MAX_HEIGHT:-480}"
JPEG_QUALITY="${JPEG_QUALITY:-45}"
MAX_UDP_BYTES="${MAX_UDP_BYTES:-55000}"
YOLO_DEVICE="${YOLO_DEVICE:-}"
YOLO_HALF="${YOLO_HALF:-true}"
METRICS_LOG_INTERVAL="${METRICS_LOG_INTERVAL:-1.0}"
ROS_DOMAIN_ID_VALUE="${ROS_DOMAIN_ID:-}"
RMW_IMPLEMENTATION_VALUE="${RMW_IMPLEMENTATION:-}"

BUILD_IMAGE=0

print_usage() {
  cat <<'EOF'
Usage:
  bash ./run_eo_topic_yolo.sh [--build]

Important environment overrides:
  IMAGE_NAME, CONTAINER_NAME, BASE_IMAGE, MODEL_NAME
  GUI_HOST, GUI_PORT
  INPUT_IMAGE_TOPIC, OUTPUT_IMAGE_TOPIC, PUBLISH_OUTPUT_TOPIC
  CONFIDENCE, INFERENCE_SIZE
  STREAM_MAX_WIDTH, STREAM_MAX_HEIGHT, JPEG_QUALITY, MAX_UDP_BYTES
  YOLO_DEVICE, YOLO_HALF, METRICS_LOG_INTERVAL
  ROS_DOMAIN_ID, RMW_IMPLEMENTATION
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

if ! sudo docker image inspect "$IMAGE_NAME" >/dev/null 2>&1; then
  BUILD_IMAGE=1
fi

if [[ "$BUILD_IMAGE" -eq 1 ]]; then
  echo "Building Docker image: $IMAGE_NAME"
  sudo docker build \
    --build-arg "BASE_IMAGE=$BASE_IMAGE" \
    --build-arg "MODEL_NAME=$MODEL_NAME" \
    -f "$SCRIPT_DIR/Dockerfile" \
    -t "$IMAGE_NAME" \
    "$SCRIPT_DIR"
fi

echo "Running EO topic YOLO bridge container"
if [[ "$IMAGE_NAME_INPUT" != "$IMAGE_NAME" ]]; then
  echo "Docker image tags must be lowercase. Using IMAGE_NAME=$IMAGE_NAME (from $IMAGE_NAME_INPUT)"
fi
echo "IMAGE_NAME=$IMAGE_NAME"
echo "CONTAINER_NAME=$CONTAINER_NAME"
echo "BASE_IMAGE=$BASE_IMAGE"
echo "MODEL_NAME=$MODEL_NAME"
echo "GUI_HOST=$GUI_HOST GUI_PORT=$GUI_PORT"
echo "INPUT_IMAGE_TOPIC=$INPUT_IMAGE_TOPIC"
echo "OUTPUT_IMAGE_TOPIC=$OUTPUT_IMAGE_TOPIC"
echo "PUBLISH_OUTPUT_TOPIC=$PUBLISH_OUTPUT_TOPIC"
echo "CONFIDENCE=$CONFIDENCE INFERENCE_SIZE=$INFERENCE_SIZE"
echo "STREAM_MAX_WIDTH=$STREAM_MAX_WIDTH STREAM_MAX_HEIGHT=$STREAM_MAX_HEIGHT JPEG_QUALITY=$JPEG_QUALITY"
echo "MAX_UDP_BYTES=$MAX_UDP_BYTES"
if [[ -n "$YOLO_DEVICE" ]]; then
  echo "YOLO_DEVICE=$YOLO_DEVICE"
fi
echo "YOLO_HALF=$YOLO_HALF"
echo "METRICS_LOG_INTERVAL=$METRICS_LOG_INTERVAL"
if [[ -n "$ROS_DOMAIN_ID_VALUE" ]]; then
  echo "ROS_DOMAIN_ID=$ROS_DOMAIN_ID_VALUE"
fi
if [[ -n "$RMW_IMPLEMENTATION_VALUE" ]]; then
  echo "RMW_IMPLEMENTATION=$RMW_IMPLEMENTATION_VALUE"
fi

if sudo docker container inspect "$CONTAINER_NAME" >/dev/null 2>&1; then
  echo "Removing previous container: $CONTAINER_NAME"
  sudo docker rm -f "$CONTAINER_NAME" >/dev/null
fi

docker_args=(
  run --rm
  --runtime nvidia
  --name "$CONTAINER_NAME"
  --network host
  --ipc host
  -e "GUI_HOST=$GUI_HOST"
  -e "GUI_PORT=$GUI_PORT"
  -e "INPUT_IMAGE_TOPIC=$INPUT_IMAGE_TOPIC"
  -e "OUTPUT_IMAGE_TOPIC=$OUTPUT_IMAGE_TOPIC"
  -e "PUBLISH_OUTPUT_TOPIC=$PUBLISH_OUTPUT_TOPIC"
  -e "CONFIDENCE=$CONFIDENCE"
  -e "INFERENCE_SIZE=$INFERENCE_SIZE"
  -e "STREAM_MAX_WIDTH=$STREAM_MAX_WIDTH"
  -e "STREAM_MAX_HEIGHT=$STREAM_MAX_HEIGHT"
  -e "JPEG_QUALITY=$JPEG_QUALITY"
  -e "MAX_UDP_BYTES=$MAX_UDP_BYTES"
  -e "YOLO_DEVICE=$YOLO_DEVICE"
  -e "YOLO_HALF=$YOLO_HALF"
  -e "METRICS_LOG_INTERVAL=$METRICS_LOG_INTERVAL"
)

if [[ -n "$ROS_DOMAIN_ID_VALUE" ]]; then
  docker_args+=(-e "ROS_DOMAIN_ID=$ROS_DOMAIN_ID_VALUE")
fi

if [[ -n "$RMW_IMPLEMENTATION_VALUE" ]]; then
  docker_args+=(-e "RMW_IMPLEMENTATION=$RMW_IMPLEMENTATION_VALUE")
fi

sudo docker "${docker_args[@]}" "$IMAGE_NAME"
