#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

IMAGE_NAME_INPUT="${IMAGE_NAME:-GUI_camera}"
IMAGE_NAME="${IMAGE_NAME_INPUT,,}"
CONTAINER_NAME="${CONTAINER_NAME:-gui_camera_bridge}"
BASE_IMAGE="${BASE_IMAGE:-minji-perception}"
WORKSPACE_DIR="${WORKSPACE_DIR:-$HOME/minji/ros2_ws}"
WORKSPACE_MOUNT_MODE="${WORKSPACE_MOUNT_MODE:-ro}"
ROS_SETUP="${ROS_SETUP:-/opt/ros/jazzy/setup.bash}"
WORKSPACE_SETUP="${WORKSPACE_SETUP:-/ros2_ws/install/setup.bash}"

GUI_HOST="${GUI_HOST:-192.168.1.94}"
EO_GUI_HOST="${EO_GUI_HOST:-$GUI_HOST}"
IR_GUI_HOST="${IR_GUI_HOST:-$GUI_HOST}"
EO_GUI_PORT="${EO_GUI_PORT:-5000}"
IR_GUI_PORT="${IR_GUI_PORT:-5001}"
EO_IMAGE_TOPIC="${EO_IMAGE_TOPIC:-/camera/eo}"
IR_IMAGE_TOPIC="${IR_IMAGE_TOPIC:-/camera/ir}"
JPEG_QUALITY="${JPEG_QUALITY:-35}"
MAX_UDP_BYTES="${MAX_UDP_BYTES:-55000}"
STREAM_WIDTH="${STREAM_WIDTH:-640}"
STREAM_HEIGHT="${STREAM_HEIGHT:-360}"
UDP_SEND_BUFFER_BYTES="${UDP_SEND_BUFFER_BYTES:-4194304}"

BUILD_IMAGE=0

print_usage() {
  cat <<'EOF'
Usage:
  bash ./run_gui_camera_yolo_node.sh [--build]

This runner is bridge-only:
- no direct camera open
- no extra YOLO inference
- subscribes existing EO/IR ROS2 image topics and forwards GUI UDP packets

Important environment overrides:
  IMAGE_NAME, CONTAINER_NAME, BASE_IMAGE
  WORKSPACE_DIR, WORKSPACE_MOUNT_MODE, ROS_SETUP, WORKSPACE_SETUP
  GUI_HOST, EO_GUI_HOST, IR_GUI_HOST
  EO_GUI_PORT, IR_GUI_PORT
  EO_IMAGE_TOPIC, IR_IMAGE_TOPIC
  JPEG_QUALITY, MAX_UDP_BYTES, STREAM_WIDTH, STREAM_HEIGHT
  UDP_SEND_BUFFER_BYTES
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

if [[ ! -d "$WORKSPACE_DIR" ]]; then
  echo "Workspace directory not found: $WORKSPACE_DIR" >&2
  exit 1
fi

if ! sudo docker image inspect "$IMAGE_NAME" >/dev/null 2>&1; then
  BUILD_IMAGE=1
fi

if [[ "$BUILD_IMAGE" -eq 1 ]]; then
  echo "Building Docker image: $IMAGE_NAME"
  sudo docker build \
    --build-arg "BASE_IMAGE=$BASE_IMAGE" \
    -f "$SCRIPT_DIR/Dockerfile.gui_camera" \
    -t "$IMAGE_NAME" \
    "$SCRIPT_DIR"
fi

echo "Running gui_camera dual bridge container"
if [[ "$IMAGE_NAME_INPUT" != "$IMAGE_NAME" ]]; then
  echo "Docker image tags must be lowercase. Using IMAGE_NAME=$IMAGE_NAME (from $IMAGE_NAME_INPUT)"
fi
echo "IMAGE_NAME=$IMAGE_NAME"
echo "CONTAINER_NAME=$CONTAINER_NAME"
echo "BASE_IMAGE=$BASE_IMAGE"
echo "WORKSPACE_DIR=$WORKSPACE_DIR"
echo "WORKSPACE_MOUNT_MODE=$WORKSPACE_MOUNT_MODE"
echo "EO_GUI_HOST=$EO_GUI_HOST EO_GUI_PORT=$EO_GUI_PORT"
echo "IR_GUI_HOST=$IR_GUI_HOST IR_GUI_PORT=$IR_GUI_PORT"
echo "EO_IMAGE_TOPIC=$EO_IMAGE_TOPIC"
echo "IR_IMAGE_TOPIC=$IR_IMAGE_TOPIC"

if sudo docker container inspect "$CONTAINER_NAME" >/dev/null 2>&1; then
  echo "Removing previous gui_camera container: $CONTAINER_NAME"
  sudo docker rm -f "$CONTAINER_NAME" >/dev/null
fi

sudo docker run --rm \
  --name "$CONTAINER_NAME" \
  --network host \
  -v "$WORKSPACE_DIR:/ros2_ws:$WORKSPACE_MOUNT_MODE" \
  -e "ROS_SETUP=$ROS_SETUP" \
  -e "WORKSPACE_SETUP=$WORKSPACE_SETUP" \
  -e "GUI_HOST=$GUI_HOST" \
  -e "EO_GUI_HOST=$EO_GUI_HOST" \
  -e "IR_GUI_HOST=$IR_GUI_HOST" \
  -e "EO_GUI_PORT=$EO_GUI_PORT" \
  -e "IR_GUI_PORT=$IR_GUI_PORT" \
  -e "EO_IMAGE_TOPIC=$EO_IMAGE_TOPIC" \
  -e "IR_IMAGE_TOPIC=$IR_IMAGE_TOPIC" \
  -e "JPEG_QUALITY=$JPEG_QUALITY" \
  -e "MAX_UDP_BYTES=$MAX_UDP_BYTES" \
  -e "STREAM_WIDTH=$STREAM_WIDTH" \
  -e "STREAM_HEIGHT=$STREAM_HEIGHT" \
  -e "UDP_SEND_BUFFER_BYTES=$UDP_SEND_BUFFER_BYTES" \
  "$IMAGE_NAME" \
  bash -lc "source \"$ROS_SETUP\" && source \"$WORKSPACE_SETUP\" && python3 /app/gui_camera_yolo_node.py"
