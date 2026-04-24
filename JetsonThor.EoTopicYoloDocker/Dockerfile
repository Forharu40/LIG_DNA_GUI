ARG BASE_IMAGE=ultralytics/ultralytics:latest-nvidia-arm64
FROM ${BASE_IMAGE}

ARG MODEL_NAME=yolo11s.pt

ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
        gnupg2 \
        lsb-release \
    && curl -sSL https://raw.githubusercontent.com/ros/rosdistro/master/ros.key \
        | gpg --dearmor -o /usr/share/keyrings/ros-archive-keyring.gpg \
    && echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/ros-archive-keyring.gpg] http://packages.ros.org/ros2/ubuntu $(. /etc/os-release && echo $UBUNTU_CODENAME) main" \
        > /etc/apt/sources.list.d/ros2.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends \
        ros-jazzy-rclpy \
        ros-jazzy-sensor-msgs \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY app/eo_topic_yolo_bridge.py /app/eo_topic_yolo_bridge.py

RUN mkdir -p /opt/models \
    && python3 -c "from ultralytics import YOLO; YOLO('${MODEL_NAME}')" \
    && cp -f ${MODEL_NAME} /opt/models/${MODEL_NAME}

ENV PYTHONUNBUFFERED=1 \
    MODEL_PATH=/opt/models/${MODEL_NAME}

CMD ["bash", "-lc", "source /opt/ros/jazzy/setup.bash && python3 /app/eo_topic_yolo_bridge.py"]
