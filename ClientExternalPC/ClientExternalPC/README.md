# Client A - 외부 프록시 클라이언트

외부 개발 PC에서 실행되는 프록시 클라이언트입니다. 브라우저나 개발 도구의 HTTP 요청을 가로채어 Relay Server를 통해 내부망에 전달합니다.

## 주요 기능

- ✅ **HTTP 프록시 서버**: 로컬 프록시 서버로 브라우저 요청 수신
- ✅ **WebSocket 클라이언트**: Relay Server와 실시간 통신
- ✅ **자동 재연결**: 연결 끊김 시 자동 복구
- ✅ **Tray Application**: 시스템 트레이에서 실행
- ✅ **설정 관리**: Relay URL 및 프록시 포트 설정

## 시스템 요구사항

- Windows 7 이상
- .NET Framework 4.5.2 이상
- Relay Server가 실행 중이어야 함

## 사용 방법

### 1. 실행

1. `ClientExternalPC.exe` 실행
2. 시스템 트레이에 아이콘이 표시됨
3. 트레이 아이콘 우클릭 → "시작" 선택

### 2. 설정

1. 트레이 아이콘 우클릭 → "설정" 선택
2. **Relay URL**: Relay Server의 WebSocket 주소
   - 예: `ws://localhost:8080/relay?type=A&token=default-token-change-in-production`
3. **프록시 포트**: 로컬 프록시 서버 포트 (기본: 8888)

### 3. 브라우저 설정

프록시를 시작한 후, 브라우저에서 다음 프록시를 설정:

- **프록시 주소**: `localhost` 또는 `127.0.0.1`
- **프록시 포트**: 설정한 포트 (기본: 8888)

#### Chrome/Edge 설정 예시:
```
설정 → 고급 → 시스템 → 프록시 서버 열기
HTTP 프록시: 127.0.0.1:8888
```

#### Firefox 설정 예시:
```
설정 → 일반 → 네트워크 설정 → 수동 프록시 설정
HTTP 프록시: 127.0.0.1, 포트: 8888
```

### 4. PAC 파일 사용 (선택사항)

특정 도메인만 프록시를 사용하려면 PAC 파일을 생성하세요:

```javascript
function FindProxyForURL(url, host) {
    // 내부망 도메인만 프록시 사용
    if (host.endsWith(".internal.company.com") || 
        host.endsWith(".company.local")) {
        return "PROXY 127.0.0.1:8888";
    }
    return "DIRECT";
}
```

## 아키텍처

```
[브라우저] → [Client A 프록시] → [Relay Server] → [Client B 에이전트] → [내부망 서버]
```

1. 브라우저가 HTTP 요청을 Client A 프록시로 전송
2. Client A가 요청을 JSON으로 변환하여 Relay Server로 전송
3. Relay Server가 Client B로 요청 전달
4. Client B가 내부망 서버로 실제 HTTP 요청 수행
5. 응답이 역순으로 전달됨

## 문제 해결

### 연결이 안 됩니다

1. Relay Server가 실행 중인지 확인
2. Relay URL이 올바른지 확인
3. 방화벽이 WebSocket 연결을 차단하지 않는지 확인
4. 트레이 아이콘을 더블클릭하여 로그 확인

### 프록시가 응답하지 않습니다

1. Client B (내부 에이전트)가 연결되어 있는지 확인
2. Relay Server 로그 확인
3. 프록시 포트가 다른 프로그램에 사용 중인지 확인

### HTTPS 연결이 안 됩니다

현재 버전은 HTTPS CONNECT 터널링을 완전히 지원하지 않습니다. HTTP 요청만 지원합니다.

## 개발자 정보

- **프로젝트**: ClientExternalPC
- **프레임워크**: .NET Framework 4.5.2
- **언어**: C#

## 라이선스

내부 사용 전용

