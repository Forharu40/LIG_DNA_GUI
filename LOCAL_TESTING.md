# Local Testing

Jetson Thor 없이도 아래 두 경로를 따로 검증할 수 있습니다.

## 1. Jetson/Ollama 응답 경로 확인

로컬 mock 서버를 먼저 실행합니다.

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Start-JetsonBridgeMock.ps1
```

그 다음 GUI를 실행합니다.

기대 결과:

- `상황 분석`에 `Jetson/Ollama 응답: Mock response...`가 추가됩니다.
- `시스템 로그`에 Jetson relay request start/success 로그가 추가됩니다.

검증되는 항목:

- C# GUI에서 TCP 요청 생성
- 요청 JSON 직렬화
- 응답 JSON 역직렬화
- 응답을 `상황 분석`에 표시하는 흐름

## 2. EO UDP 수신 경로 확인

GUI를 켠 상태에서 다른 터미널에서 테스트 프레임을 보냅니다.

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Send-EoUdpTestFrame.ps1
```

기대 결과:

- EO placeholder 대신 `EO UDP TEST`가 적힌 움직이는 이미지가 표시됩니다.
- `시스템 로그`에 `EO UDP camera first frame received.`가 추가됩니다.

검증되는 항목:

- UDP 포트 5000 바인딩
- 헤더 파싱
- JPEG 디코딩
- EO 메인 화면 렌더링

## 3. 무엇이 확인되고 무엇이 아직 미확인인가

이 로컬 테스트가 통과하면 아래는 확인된 상태입니다.

- GUI 로직
- PC 내부 TCP/UDP 수신 처리
- Jetson 응답 표시 UI
- EO 프레임 표시 UI

아직 실제 장비 연결 전에는 아래는 미확인입니다.

- 실제 Jetson IP/방화벽/라우팅
- Jetson Thor에서 돌아가는 C++ 브리지 실행 환경
- Jetson 내부 Ollama 모델 설치 상태
- 실제 EO 송신기 패킷 형식과 완전 일치 여부
