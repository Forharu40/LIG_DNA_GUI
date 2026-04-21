#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

GUI_HOST="${GUI_HOST:-192.168.1.94}"
GUI_PORT="${GUI_PORT:-5001}"
IMAGE_TOPIC="${IMAGE_TOPIC:-/yolo/ir/image_raw}"
DETECTION_TOPIC="${DETECTION_TOPIC:-/detections/ir}"
STATUS_TOPIC="${STATUS_TOPIC:-/yolo/ir/status}"
JPEG_QUALITY="${JPEG_QUALITY:-45}"
MAX_UDP_BYTES="${MAX_UDP_BYTES:-55000}"
STREAM_MAX_WIDTH="${STREAM_MAX_WIDTH:-854}"
STREAM_MAX_HEIGHT="${STREAM_MAX_HEIGHT:-480}"
UDP_SEND_BUFFER_BYTES="${UDP_SEND_BUFFER_BYTES:-4194304}"
MIN_DETECTION_SCORE="${MIN_DETECTION_SCORE:-0.0}"

ROS_SETUP="${ROS_SETUP:-/opt/ros/humble/setup.bash}"
WORKSPACE_SETUP="${WORKSPACE_SETUP:-/ros2_ws/install/setup.bash}"

print_usage() {
  cat <<'EOF'
Usage:
  bash ./run_ir_gui_bridge.sh

Environment overrides:
  GUI_HOST, GUI_PORT
  IMAGE_TOPIC, DETECTION_TOPIC, STATUS_TOPIC
  JPEG_QUALITY, MAX_UDP_BYTES, STREAM_MAX_WIDTH, STREAM_MAX_HEIGHT
  UDP_SEND_BUFFER_BYTES, MIN_DETECTION_SCORE
  ROS_SETUP, WORKSPACE_SETUP
EOF
}

case "${1:-}" in
  --help|-h)
    print_usage
    exit 0
    ;;
  "")
    ;;
  *)
    echo "Unknown option: $1" >&2
    print_usage
    exit 1
    ;;
esac

if [[ -f "$ROS_SETUP" ]]; then
  # shellcheck disable=SC1090
  source "$ROS_SETUP"
fi

if [[ -f "$WORKSPACE_SETUP" ]]; then
  # shellcheck disable=SC1090
  source "$WORKSPACE_SETUP"
fi

export GUI_HOST GUI_PORT IMAGE_TOPIC DETECTION_TOPIC STATUS_TOPIC
export JPEG_QUALITY MAX_UDP_BYTES STREAM_MAX_WIDTH STREAM_MAX_HEIGHT
export UDP_SEND_BUFFER_BYTES MIN_DETECTION_SCORE

echo "Running IR ROS2 -> GUI bridge"
echo "GUI_HOST=$GUI_HOST GUI_PORT=$GUI_PORT"
echo "IMAGE_TOPIC=$IMAGE_TOPIC"
echo "DETECTION_TOPIC=$DETECTION_TOPIC"
echo "STATUS_TOPIC=$STATUS_TOPIC"

python3 "$SCRIPT_DIR/app/ros_ir_to_gui_bridge.py"
