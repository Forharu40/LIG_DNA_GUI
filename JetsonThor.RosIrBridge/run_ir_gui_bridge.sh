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

PYTHON_BIN="${PYTHON_BIN:-python3}"
ROS_SETUP="${ROS_SETUP:-}"
WORKSPACE_SETUP="${WORKSPACE_SETUP:-}"

detect_ros_setup() {
  if [[ -n "$ROS_SETUP" && -f "$ROS_SETUP" ]]; then
    echo "$ROS_SETUP"
    return 0
  fi

  local candidates=(
    "/opt/ros/humble/setup.bash"
    "/opt/ros/jazzy/setup.bash"
    "/opt/ros/iron/setup.bash"
    "/opt/ros/rolling/setup.bash"
    "/opt/ros/foxy/setup.bash"
  )

  local candidate
  for candidate in "${candidates[@]}"; do
    if [[ -f "$candidate" ]]; then
      echo "$candidate"
      return 0
    fi
  done

  return 1
}

safe_source_setup() {
  local setup_path="$1"

  if [[ ! -f "$setup_path" ]]; then
    echo "Setup file not found: $setup_path" >&2
    return 1
  fi

  # ROS setup 스크립트 일부는 비어 있는 환경변수를 참조하므로
  # source 직전에는 nounset(-u)을 잠시 끄고, 끝나면 다시 복구한다.
  local had_nounset=0
  case $- in
    *u*) had_nounset=1 ;;
  esac

  set +u
  # shellcheck disable=SC1090
  source "$setup_path"
  local source_exit=$?

  if [[ $had_nounset -eq 1 ]]; then
    set -u
  fi

  return $source_exit
}

detect_workspace_setup() {
  if [[ -n "$WORKSPACE_SETUP" && -f "$WORKSPACE_SETUP" ]]; then
    echo "$WORKSPACE_SETUP"
    return 0
  fi

  local candidates=(
    "/ros2_ws/install/setup.bash"
    "$HOME/ros2_ws/install/setup.bash"
    "$HOME/sentinel_ws/install/setup.bash"
    "$HOME/colcon_ws/install/setup.bash"
    "$HOME/LIG_DNA_GUI/install/setup.bash"
  )

  local candidate
  for candidate in "${candidates[@]}"; do
    if [[ -f "$candidate" ]]; then
      echo "$candidate"
      return 0
    fi
  done

  return 1
}

print_usage() {
  cat <<'EOF'
Usage:
  bash ./run_ir_gui_bridge.sh

Environment overrides:
  GUI_HOST, GUI_PORT
  IMAGE_TOPIC, DETECTION_TOPIC, STATUS_TOPIC
  JPEG_QUALITY, MAX_UDP_BYTES, STREAM_MAX_WIDTH, STREAM_MAX_HEIGHT
  UDP_SEND_BUFFER_BYTES, MIN_DETECTION_SCORE
  ROS_SETUP, WORKSPACE_SETUP, PYTHON_BIN
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

if ! ROS_SETUP="$(detect_ros_setup)"; then
  echo "ROS2 setup.bash could not be found. Set ROS_SETUP explicitly." >&2
  exit 1
fi

if ! safe_source_setup "$ROS_SETUP"; then
  echo "Failed to source ROS setup: $ROS_SETUP" >&2
  exit 1
fi

if ! WORKSPACE_SETUP="$(detect_workspace_setup)"; then
  echo "Workspace setup.bash could not be found. Set WORKSPACE_SETUP explicitly." >&2
  exit 1
fi

if ! safe_source_setup "$WORKSPACE_SETUP"; then
  echo "Failed to source workspace setup: $WORKSPACE_SETUP" >&2
  exit 1
fi

export GUI_HOST GUI_PORT IMAGE_TOPIC DETECTION_TOPIC STATUS_TOPIC
export JPEG_QUALITY MAX_UDP_BYTES STREAM_MAX_WIDTH STREAM_MAX_HEIGHT
export UDP_SEND_BUFFER_BYTES MIN_DETECTION_SCORE

echo "Running IR ROS2 -> GUI bridge"
echo "GUI_HOST=$GUI_HOST GUI_PORT=$GUI_PORT"
echo "IMAGE_TOPIC=$IMAGE_TOPIC"
echo "DETECTION_TOPIC=$DETECTION_TOPIC"
echo "STATUS_TOPIC=$STATUS_TOPIC"
echo "ROS_SETUP=$ROS_SETUP"
echo "WORKSPACE_SETUP=$WORKSPACE_SETUP"
echo "PYTHON_BIN=$PYTHON_BIN"

if ! command -v "$PYTHON_BIN" >/dev/null 2>&1; then
  echo "Python interpreter not found: $PYTHON_BIN" >&2
  exit 1
fi

if ! "$PYTHON_BIN" - <<'PY'
import importlib
import sys

required = [
    "rclpy",
    "sensor_msgs.msg",
    "sentinel_interfaces.msg",
]

missing = []
for name in required:
    try:
        importlib.import_module(name)
    except Exception as exc:
        missing.append((name, str(exc)))

if missing:
    for name, reason in missing:
        print(f"Missing Python module: {name} ({reason})", file=sys.stderr)
    sys.exit(1)
PY
then
  echo "ROS2 Python environment is not ready. Check ROS_SETUP and WORKSPACE_SETUP." >&2
  exit 1
fi

"$PYTHON_BIN" "$SCRIPT_DIR/app/ros_ir_to_gui_bridge.py"
