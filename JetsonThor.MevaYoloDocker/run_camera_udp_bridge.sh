#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

IMAGE_NAME_INPUT="${IMAGE_NAME:-GUI_camera_bridge}"
IMAGE_NAME="${IMAGE_NAME_INPUT,,}"
CONTAINER_NAME="${CONTAINER_NAME:-gui_camera_bridge}"
BASE_IMAGE="${BASE_IMAGE:-ros:jazzy-ros-base}"

GUI_HOST="${GUI_HOST:-192.168.1.94}"
EO_GUI_PORT="${EO_GUI_PORT:-5000}"
IR_GUI_PORT="${IR_GUI_PORT:-5001}"
EO_IMAGE_TOPIC="${EO_IMAGE_TOPIC:-/camera/eo}"
IR_IMAGE_TOPIC="${IR_IMAGE_TOPIC:-/camera/ir}"
STREAM_WIDTH="${STREAM_WIDTH:-640}"
STREAM_HEIGHT="${STREAM_HEIGHT:-360}"
JPEG_QUALITY="${JPEG_QUALITY:-35}"
UDP_SEND_BUFFER_BYTES="${UDP_SEND_BUFFER_BYTES:-4194304}"
ROS_DOMAIN_ID_VALUE="${ROS_DOMAIN_ID:-}"
RMW_IMPLEMENTATION_VALUE="${RMW_IMPLEMENTATION:-}"

BUILD_IMAGE=0

print_usage() {
  cat <<'EOF'
Usage:
  bash ./run_camera_udp_bridge.sh [--build]

This runner uses a dedicated Docker image and does not depend on minji containers.

Important environment overrides:
  IMAGE_NAME, CONTAINER_NAME, BASE_IMAGE
  GUI_HOST, EO_GUI_PORT, IR_GUI_PORT
  EO_IMAGE_TOPIC, IR_IMAGE_TOPIC
  STREAM_WIDTH, STREAM_HEIGHT, JPEG_QUALITY
  UDP_SEND_BUFFER_BYTES
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
    -f "$SCRIPT_DIR/Dockerfile.camera_udp_bridge" \
    -t "$IMAGE_NAME" \
    "$SCRIPT_DIR"
fi

echo "Running camera UDP bridge container"
if [[ "$IMAGE_NAME_INPUT" != "$IMAGE_NAME" ]]; then
  echo "Docker image tags must be lowercase. Using IMAGE_NAME=$IMAGE_NAME (from $IMAGE_NAME_INPUT)"
fi
echo "IMAGE_NAME=$IMAGE_NAME"
echo "CONTAINER_NAME=$CONTAINER_NAME"
echo "BASE_IMAGE=$BASE_IMAGE"
echo "GUI_HOST=$GUI_HOST"
echo "EO_IMAGE_TOPIC=$EO_IMAGE_TOPIC EO_GUI_PORT=$EO_GUI_PORT"
echo "IR_IMAGE_TOPIC=$IR_IMAGE_TOPIC IR_GUI_PORT=$IR_GUI_PORT"
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
  --name "$CONTAINER_NAME"
  --network host
  --ipc host
  -e "GUI_HOST=$GUI_HOST"
  -e "EO_GUI_PORT=$EO_GUI_PORT"
  -e "IR_GUI_PORT=$IR_GUI_PORT"
  -e "EO_IMAGE_TOPIC=$EO_IMAGE_TOPIC"
  -e "IR_IMAGE_TOPIC=$IR_IMAGE_TOPIC"
  -e "STREAM_WIDTH=$STREAM_WIDTH"
  -e "STREAM_HEIGHT=$STREAM_HEIGHT"
  -e "JPEG_QUALITY=$JPEG_QUALITY"
  -e "UDP_SEND_BUFFER_BYTES=$UDP_SEND_BUFFER_BYTES"
)

if [[ -n "$ROS_DOMAIN_ID_VALUE" ]]; then
  docker_args+=(-e "ROS_DOMAIN_ID=$ROS_DOMAIN_ID_VALUE")
fi

if [[ -n "$RMW_IMPLEMENTATION_VALUE" ]]; then
  docker_args+=(-e "RMW_IMPLEMENTATION=$RMW_IMPLEMENTATION_VALUE")
fi

sudo docker "${docker_args[@]}" "$IMAGE_NAME"
