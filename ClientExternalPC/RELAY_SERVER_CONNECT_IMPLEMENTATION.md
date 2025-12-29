# Relay Server CONNECT 터널링 구현 가이드

## 개요

현재 Client A (ProxyClientA)는 필터링 대상 HTTPS 요청을 Relay Server로 전달하도록 수정되었습니다. 하지만 Relay Server가 바이너리 스트리밍을 지원하지 않으면 실제 터널링이 동작하지 않습니다.

이 문서는 Relay Server에서 CONNECT 터널링을 위한 바이너리 스트리밍을 구현하는 방법을 설명합니다.

---

## 현재 상황

### Client A의 동작

1. **필터링 체크**: HTTPS CONNECT 요청이 필터링 대상인지 확인
2. **Relay Server 전달**: 필터링 대상이면 Relay Server로 CONNECT 요청 전송
3. **응답 대기**: Relay Server로부터 응답 대기
4. **터널링 시작**: 200 OK 응답을 받으면 터널링 시작

### 현재 문제점

- Relay Server는 JSON 메시지만 처리하므로 바이너리 스트리밍을 지원하지 않음
- CONNECT 터널링은 바이너리 데이터를 실시간으로 스트리밍해야 함
- 현재 구조로는 실제 HTTPS 통신이 불가능

---

## 구현 요구사항

### 1. CONNECT 요청 처리

#### Client A → Relay Server 메시지 형식

```json
{
  "type": "CONNECT",
  "sessionId": "guid-string",
  "method": "CONNECT",
  "url": "https://example.com:443",
  "headers": {
    "Host": "example.com:443",
    "User-Agent": "Mozilla/5.0..."
  }
}
```

#### Relay Server → Client B 전달

Relay Server는 Client B로 동일한 CONNECT 요청을 전달해야 합니다.

#### Client B → Relay Server 응답

```json
{
  "type": "CONNECT_RESPONSE",
  "sessionId": "guid-string",
  "statusCode": 200,
  "statusDescription": "Connection Established"
}
```

또는 실패 시:

```json
{
  "type": "CONNECT_RESPONSE",
  "sessionId": "guid-string",
  "statusCode": 502,
  "error": "Connection failed"
}
```

### 2. 바이너리 스트리밍 모드 전환

CONNECT 요청이 성공하면 (200 OK), 이후의 통신은 JSON 메시지가 아닌 **바이너리 스트리밍 모드**로 전환되어야 합니다.

#### WebSocket 메시지 타입 변경

- **일반 HTTP 요청**: `WebSocketMessageType.Text` (JSON)
- **CONNECT 터널링**: `WebSocketMessageType.Binary` (바이너리 데이터)

#### 세션 상태 관리

Relay Server는 각 `sessionId`에 대해 다음 상태를 관리해야 합니다:

1. **REQUEST**: 일반 HTTP 요청 (JSON)
2. **CONNECT_REQUEST**: CONNECT 요청 처리 중 (JSON)
3. **TUNNELING**: 바이너리 스트리밍 모드 (Binary)

---

## 구현 단계

### 단계 1: CONNECT 요청 처리

#### Relay Server (중계 서버)

```csharp
// CONNECT 요청 수신 시
if (message.Type == "CONNECT")
{
    // Client B로 CONNECT 요청 전달
    await SendToClientB(new RelayMessage
    {
        Type = "CONNECT",
        SessionId = message.SessionId,
        Method = "CONNECT",
        Url = message.Url,
        Headers = message.Headers
    });
    
    // 세션 상태를 CONNECT_REQUEST로 설정
    _sessions[message.SessionId] = SessionState.ConnectRequest;
}
```

#### Client B (Agent)

```csharp
// CONNECT 요청 처리
if (message.Type == "CONNECT")
{
    var targetUri = new Uri(message.Url);
    var targetHost = targetUri.Host;
    var targetPort = targetUri.Port;
    
    try
    {
        // 대상 서버에 TCP 연결
        var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(targetHost, targetPort);
        
        // Relay Server로 성공 응답
        await SendToRelayServer(new RelayMessage
        {
            Type = "CONNECT_RESPONSE",
            SessionId = message.SessionId,
            StatusCode = 200,
            StatusDescription = "Connection Established"
        });
        
        // 세션 상태를 TUNNELING으로 변경
        _sessions[message.SessionId] = new TunnelSession
        {
            State = SessionState.Tunneling,
            TcpClient = tcpClient,
            NetworkStream = tcpClient.GetStream()
        };
    }
    catch (Exception ex)
    {
        // 실패 응답
        await SendToRelayServer(new RelayMessage
        {
            Type = "CONNECT_RESPONSE",
            SessionId = message.SessionId,
            StatusCode = 502,
            Error = ex.Message
        });
    }
}
```

### 단계 2: 바이너리 스트리밍 모드 전환

#### Relay Server

```csharp
// CONNECT_RESPONSE 수신 시
if (message.Type == "CONNECT_RESPONSE" && message.StatusCode == 200)
{
    // 세션 상태를 TUNNELING으로 변경
    _sessions[message.SessionId].State = SessionState.Tunneling;
    
    // Client A로 성공 응답 전송
    await SendToClientA(new RelayMessage
    {
        Type = "CONNECT_RESPONSE",
        SessionId = message.SessionId,
        StatusCode = 200,
        StatusDescription = "Connection Established"
    });
}
```

#### 이후 통신

세션 상태가 `TUNNELING`이면, 모든 WebSocket 메시지는 `WebSocketMessageType.Binary`로 처리해야 합니다.

### 단계 3: 바이너리 데이터 릴레이

#### Client A → Relay Server → Client B

```csharp
// Client A에서 바이너리 데이터 수신
if (session.State == SessionState.Tunneling)
{
    var binaryData = await ReceiveBinaryFromClientA(sessionId);
    
    // Client B로 바이너리 데이터 전달
    await SendBinaryToClientB(sessionId, binaryData);
}
```

#### Client B → Relay Server → Client A

```csharp
// Client B에서 대상 서버로부터 데이터 수신
var serverData = await ReadFromTargetServer(session.TcpClient);

// Relay Server로 바이너리 데이터 전달
await SendBinaryToRelayServer(sessionId, serverData);

// Relay Server는 Client A로 바이너리 데이터 전달
await SendBinaryToClientA(sessionId, serverData);
```

---

## WebSocket 메시지 프로토콜

### 바이너리 메시지 형식

바이너리 스트리밍 모드에서는 각 메시지에 `sessionId`를 포함해야 합니다.

#### 제안 형식 1: 헤더 + 데이터

```
[4 bytes: sessionId 길이]
[sessionId (UTF-8)]
[4 bytes: 데이터 길이]
[데이터]
```

#### 제안 형식 2: JSON 헤더 + 바이너리 데이터

첫 번째 WebSocket 프레임: JSON 헤더
```json
{
  "type": "TUNNEL_DATA",
  "sessionId": "guid-string",
  "length": 1024
}
```

두 번째 WebSocket 프레임: 바이너리 데이터
```
[바이너리 데이터]
```

**권장**: 형식 2 (JSON 헤더 + 바이너리 데이터)
- WebSocket 프레임 분리로 구현이 간단함
- 세션 관리가 명확함

---

## 세션 관리

### 세션 상태 전이도

```
REQUEST (JSON)
    ↓
CONNECT_REQUEST (JSON) ← CONNECT 요청 수신
    ↓
TUNNELING (Binary) ← CONNECT 성공 (200 OK)
    ↓
CLOSED ← 연결 종료
```

### 세션 타임아웃

- **CONNECT_REQUEST**: 10초 내 응답 없으면 타임아웃
- **TUNNELING**: 연결이 끊어질 때까지 유지 (또는 명시적 종료 메시지)

### 세션 정리

연결 종료 시:
1. TCP 연결 종료
2. WebSocket 연결 종료 (해당 세션)
3. 세션 상태를 CLOSED로 변경
4. 세션 정보 제거

---

## 에러 처리

### CONNECT 실패

```json
{
  "type": "CONNECT_RESPONSE",
  "sessionId": "guid-string",
  "statusCode": 502,
  "error": "Connection to target server failed: connection timeout"
}
```

### 터널링 중 오류

```json
{
  "type": "TUNNEL_ERROR",
  "sessionId": "guid-string",
  "error": "Connection lost"
}
```

에러 발생 시:
1. Client A에 에러 메시지 전송
2. Client B의 TCP 연결 종료
3. 세션 정리

---

## 구현 체크리스트

### Relay Server

- [ ] CONNECT 요청 타입 처리
- [ ] Client B로 CONNECT 요청 전달
- [ ] CONNECT_RESPONSE 수신 및 Client A로 전달
- [ ] 세션 상태 관리 (REQUEST → CONNECT_REQUEST → TUNNELING)
- [ ] 바이너리 스트리밍 모드 전환
- [ ] 바이너리 데이터 릴레이 (Client A ↔ Client B)
- [ ] 세션 타임아웃 처리
- [ ] 연결 종료 처리
- [ ] 에러 처리

### Client B (Agent)

- [ ] CONNECT 요청 처리
- [ ] 대상 서버에 TCP 연결
- [ ] CONNECT_RESPONSE 전송
- [ ] 바이너리 데이터 수신 (대상 서버 → Relay Server)
- [ ] 바이너리 데이터 전송 (Relay Server → 대상 서버)
- [ ] 연결 종료 처리

### Client A (ProxyClientA)

- [x] 필터링 체크 추가
- [x] CONNECT 요청을 Relay Server로 전달
- [x] CONNECT_RESPONSE 수신
- [ ] 바이너리 스트리밍 모드 전환 (Relay Server 지원 후)
- [ ] 바이너리 데이터 릴레이 (브라우저 ↔ Relay Server)

---

## 테스트 시나리오

### 1. 필터링 대상 HTTPS 사이트 접속

```
브라우저 → Client A → Relay Server → Client B → 대상 서버
```

1. 브라우저가 `https://example.com` 접속 시도
2. Client A가 필터링 체크 (example.com이 필터 목록에 있음)
3. Client A가 Relay Server로 CONNECT 요청 전송
4. Relay Server가 Client B로 CONNECT 요청 전달
5. Client B가 대상 서버에 TCP 연결
6. Client B가 Relay Server로 200 OK 응답
7. Relay Server가 Client A로 200 OK 응답
8. 바이너리 스트리밍 모드로 전환
9. HTTPS 통신 시작 (TLS 핸드셰이크 등)

### 2. 필터링 대상이 아닌 HTTPS 사이트 접속

```
브라우저 → Client A → 대상 서버 (직접 연결)
```

1. 브라우저가 `https://github.com` 접속 시도
2. Client A가 필터링 체크 (github.com이 필터 목록에 없음)
3. Client A가 대상 서버에 직접 TCP 연결
4. 직접 터널링 시작

---

## 주의사항

1. **WebSocket 프레임 크기**: 큰 파일 전송 시 WebSocket 프레임이 분할될 수 있으므로 청크 단위로 처리 필요

2. **버퍼링**: 바이너리 데이터는 버퍼링하지 않고 즉시 릴레이해야 함 (지연 최소화)

3. **동시 세션**: 여러 HTTPS 연결이 동시에 발생할 수 있으므로 세션 관리가 중요함

4. **리소스 정리**: 연결 종료 시 TCP 소켓, WebSocket 연결, 세션 정보를 모두 정리해야 함

5. **에러 복구**: 네트워크 오류 발생 시 적절한 에러 메시지를 Client A에 전달해야 함

---

## 참고 자료

- [RFC 7231 - HTTP/1.1 Semantics and Content](https://tools.ietf.org/html/rfc7231#section-4.3.6)
- [WebSocket Protocol (RFC 6455)](https://tools.ietf.org/html/rfc6455)
- [HTTP CONNECT Method](https://developer.mozilla.org/en-US/docs/Web/HTTP/Methods/CONNECT)

---

## 작성일

2025-12-23

## 작성자

Client A 개발팀

---

## 업데이트 이력

- 2025-12-23: 초안 작성 (Client A에서 필터링 체크 추가 후)

