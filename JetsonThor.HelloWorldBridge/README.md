# JetsonThor.HelloWorldBridge

Jetson Thor에서 실행하는 `hello -> world` 전용 C++ 브리지입니다.

현재 단계에서는 GUI 쪽 통신 검증이 목적이므로, Ollama를 직접 호출하지 않고
운용통제 GUI가 3초마다 보내는 더미 신호 `"Hello"`에 대해 `"world"`를 응답합니다.

## 역할

- 운용통제 PC의 C# GUI 요청 수신
- 요청 JSON에서 `requestId`, `prompt` 읽기
- `prompt`가 `hello` 계열이면 `world` 응답 반환
- 그 외 요청은 오류 또는 기본 메시지 반환

## 통신 포맷

GUI가 보내는 형식:

```json
{"requestId":"abc123","model":"heartbeat","prompt":"Hello"}
```

Jetson이 돌려주는 형식:

```json
{"requestId":"abc123","status":"ok","response":"world","error":"","elapsedMs":1}
```

한 요청과 한 응답은 모두 `JSON 1줄 + 줄바꿈` 형식입니다.

## 빌드

```bash
cmake -S . -B build
cmake --build build
```

## 실행

기본 포트는 `7001`입니다.

```bash
./build/jetson_hello_world_bridge
```

포트를 바꾸고 싶으면:

```bash
./build/jetson_hello_world_bridge --port 7001
```

## GUI와 연결

GUI는 기본적으로 `127.0.0.1:7001`로 접속합니다.
실제 Jetson Thor IP를 쓸 경우 운용통제 PC에서 아래 환경변수를 설정하면 됩니다.

- `JETSON_BRIDGE_HOST`
- `JETSON_BRIDGE_PORT`

## 확장 방향

현재 프로젝트는 통신 검증용입니다.
나중에 실제 Jetson 단계로 넘어가면 이 프로젝트를 기반으로 다음 기능을 붙일 수 있습니다.

- 센서 데이터 결합
- 실제 상태 문장 생성
- Ollama REST API 호출
- 요청 유형별 분기 처리
