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
GUI_PORT="${GUI_PORT:-5000}"
IMAGE_TOPIC="${IMAGE_TOPIC:-/yolo/eo/image_raw}"
DETECTION_TOPIC="${DETECTION_TOPIC:-/detections/eo}"
STATUS_TOPIC="${STATUS_TOPIC:-/yolo/eo/status}"
SOURCE_NAME="${SOURCE_NAME:-eo}"
JPEG_QUALITY="${JPEG_QUALITY:-45}"
MAX_UDP_BYTES="${MAX_UDP_BYTES:-55000}"
STREAM_MAX_WIDTH="${STREAM_MAX_WIDTH:-854}"
STREAM_MAX_HEIGHT="${STREAM_MAX_HEIGHT:-480}"
UDP_SEND_BUFFER_BYTES="${UDP_SEND_BUFFER_BYTES:-4194304}"
MIN_DETECTION_SCORE="${MIN_DETECTION_SCORE:-0.0}"

BUILD_IMAGE=0

print_usage() {
  cat <<'EOF'
Usage:
  bash ./run_gui_camera_yolo_node.sh [--build]

This runner is bridge-only:
- no direct camera open
- no extra YOLO inference
- subscribes existing EO ROS2 topics and forwards GUI UDP packets

Important environment overrides:
  IMAGE_NAME, CONTAINER_NAME, BASE_IMAGE
  WORKSPACE_DIR, WORKSPACE_MOUNT_MODE, ROS_SETUP, WORKSPACE_SETUP
  GUI_HOST, GUI_PORT
  IMAGE_TOPIC, DETECTION_TOPIC, STATUS_TOPIC, SOURCE_NAME
  JPEG_QUALITY, MAX_UDP_BYTES, STREAM_MAX_WIDTH, STREAM_MAX_HEIGHT
  UDP_SEND_BUFFER_BYTES, MIN_DETECTION_SCORE
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

echo "Running gui_camera bridge container"
if [[ "$IMAGE_NAME_INPUT" != "$IMAGE_NAME" ]]; then
  echo "Docker image tags must be lowercase. Using IMAGE_NAME=$IMAGE_NAME (from $IMAGE_NAME_INPUT)"
fi
echo "IMAGE_NAME=$IMAGE_NAME"
echo "CONTAINER_NAME=$CONTAINER_NAME"
echo "BASE_IMAGE=$BASE_IMAGE"
echo "WORKSPACE_DIR=$WORKSPACE_DIR"
echo "WORKSPACE_MOUNT_MODE=$WORKSPACE_MOUNT_MODE"
echo "GUI_HOST=$GUI_HOST GUI_PORT=$GUI_PORT"
echo "IMAGE_TOPIC=$IMAGE_TOPIC"
echo "DETECTION_TOPIC=$DETECTION_TOPIC"
echo "STATUS_TOPIC=$STATUS_TOPIC"

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
  -e "GUI_PORT=$GUI_PORT" \
  -e "IMAGE_TOPIC=$IMAGE_TOPIC" \
  -e "DETECTION_TOPIC=$DETECTION_TOPIC" \
  -e "STATUS_TOPIC=$STATUS_TOPIC" \
  -e "SOURCE_NAME=$SOURCE_NAME" \
  -e "JPEG_QUALITY=$JPEG_QUALITY" \
  -e "MAX_UDP_BYTES=$MAX_UDP_BYTES" \
  -e "STREAM_MAX_WIDTH=$STREAM_MAX_WIDTH" \
  -e "STREAM_MAX_HEIGHT=$STREAM_MAX_HEIGHT" \
  -e "UDP_SEND_BUFFER_BYTES=$UDP_SEND_BUFFER_BYTES" \
  -e "MIN_DETECTION_SCORE=$MIN_DETECTION_SCORE" \
  "$IMAGE_NAME" \
  bash -lc "source \"$ROS_SETUP\" && source \"$WORKSPACE_SETUP\" && python3 /app/gui_camera_yolo_node.py"
