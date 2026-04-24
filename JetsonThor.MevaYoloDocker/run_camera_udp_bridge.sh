#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

ROS_SETUP="${ROS_SETUP:-/opt/ros/jazzy/setup.bash}"
WORKSPACE_SETUP="${WORKSPACE_SETUP:-$HOME/gui_camera_ws/install/local_setup.bash}"
PYTHON_BIN="${PYTHON_BIN:-python3}"

GUI_HOST="${GUI_HOST:-192.168.1.94}"
EO_GUI_PORT="${EO_GUI_PORT:-5000}"
IR_GUI_PORT="${IR_GUI_PORT:-5001}"
EO_IMAGE_TOPIC="${EO_IMAGE_TOPIC:-/camera/eo}"
IR_IMAGE_TOPIC="${IR_IMAGE_TOPIC:-/camera/ir}"
STREAM_WIDTH="${STREAM_WIDTH:-640}"
STREAM_HEIGHT="${STREAM_HEIGHT:-360}"
JPEG_QUALITY="${JPEG_QUALITY:-35}"
UDP_SEND_BUFFER_BYTES="${UDP_SEND_BUFFER_BYTES:-4194304}"

if [[ ! -f "$ROS_SETUP" ]]; then
  echo "ROS setup.bash not found: $ROS_SETUP" >&2
  exit 1
fi

if [[ ! -f "$WORKSPACE_SETUP" ]]; then
  echo "Workspace setup.bash not found: $WORKSPACE_SETUP" >&2
  exit 1
fi

echo "Running ROS2 camera -> GUI UDP bridge"
echo "GUI_HOST=$GUI_HOST"
echo "EO_IMAGE_TOPIC=$EO_IMAGE_TOPIC EO_GUI_PORT=$EO_GUI_PORT"
echo "IR_IMAGE_TOPIC=$IR_IMAGE_TOPIC IR_GUI_PORT=$IR_GUI_PORT"
echo "WORKSPACE_SETUP=$WORKSPACE_SETUP"
echo "PYTHON_BIN=$PYTHON_BIN"

export GUI_HOST
export EO_GUI_PORT
export IR_GUI_PORT
export EO_IMAGE_TOPIC
export IR_IMAGE_TOPIC
export STREAM_WIDTH
export STREAM_HEIGHT
export JPEG_QUALITY
export UDP_SEND_BUFFER_BYTES

source "$ROS_SETUP"
source "$WORKSPACE_SETUP"
exec "$PYTHON_BIN" "$SCRIPT_DIR/app/camera_udp_bridge.py"
