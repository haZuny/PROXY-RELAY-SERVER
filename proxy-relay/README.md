# Proxy Relay Server - 클라이언트 개발 가이드

## 📋 목차

1. [프로젝트 개요](#프로젝트-개요)
2. [시스템 아키텍처](#시스템-아키텍처)
3. [서버 정보](#서버-정보)
4. [WebSocket 연결](#websocket-연결)
5. [인증](#인증)
6. [메시지 포맷](#메시지-포맷)
7. [Client A 개발 가이드](#client-a-개발-가이드)
8. [Client B 개발 가이드](#client-b-개발-가이드)
9. [에러 처리](#에러-처리)
10. [예제 코드](#예제-코드)
11. [FAQ](#faq)

---

## 📖 프로젝트 개요

**Proxy Relay Server**는 내부망과 외부망 간의 안전한 통신을 위한 Reverse Tunnel 기반 중계 서버입니다.

### 핵심 특징

- ✅ **Outbound 연결만 사용**: Inbound 포트 오픈 없이 안전한 통신
- ✅ **WebSocket 기반**: 실시간 양방향 통신
- ✅ **Reverse Tunnel 방식**: Chrome Remote Desktop, TeamViewer와 동일한 원리
- ✅ **자동 재연결**: 네트워크 끊김 시 자동 복구

### 시스템 구성

```
[외부 개발 PC] ←→ [Relay Server] ←→ [내부 작업 PC]
   Client A          (중계 서버)        Client B
```

---

## 🏗️ 시스템 아키텍처

### 통신 흐름

```
1. 초기 연결
   ┌─────────────────────────────────────────┐
   │ 내부 PC Agent B → Relay Server (Outbound)│
   │ 연결 유지 (지속 연결)                     │
   └─────────────────────────────────────────┘

2. 요청/응답 전달
   ┌─────────────────────────────────────────┐
   │ 브라우저 → Proxy A → Relay → Agent B    │
   │         → 내부망 서버                    │
   │                                         │
   │ 응답: 내부망 서버 → Agent B → Relay     │
   │      → Proxy A → 브라우저               │
   └─────────────────────────────────────────┘
```

### 컴포넌트 역할

| 컴포넌트 | 역할 | 설명 |
|---------|------|------|
| **Client A** | 외부 프록시 | 외부 개발 PC에서 실행, 브라우저 요청을 중계 |
| **Relay Server** | 중계 서버 | Client A와 Client B 간 메시지 라우팅 |
| **Client B** | 내부 에이전트 | 내부 작업 PC에서 실행, 실제 HTTP 요청 수행 |

---

## 🌐 서버 정보

### 기본 설정

- **서버 주소**: `ws://localhost:8080` (개발 환경)
- **WebSocket 엔드포인트**: `/relay`
- **프로토콜**: WebSocket (ws://) 또는 WebSocket Secure (wss://)
- **기본 포트**: `8080`

### 프로덕션 환경

프로덕션 환경에서는 다음을 변경해야 합니다:

```properties
# application.properties
server.port=443  # 또는 8443
relay.access-token=YOUR_SECURE_TOKEN_HERE
```

**중요**: 프로덕션에서는 반드시 **wss://** (TLS)를 사용하세요.

---

## 🔌 WebSocket 연결

### 연결 URL 형식

```
ws://[서버주소]:[포트]/relay?type=[A|B]&token=[액세스토큰]
```

### 예시

```javascript
// Client A (외부 프록시)
const wsUrl = 'ws://relay.example.com:8080/relay?type=A&token=default-token-change-in-production';

// Client B (내부 에이전트)
const wsUrl = 'ws://relay.example.com:8080/relay?type=B&token=default-token-change-in-production';
```

### 연결 파라미터

| 파라미터 | 필수 | 설명 | 값 |
|---------|------|------|-----|
| `type` | ✅ | 클라이언트 타입 | `A` (외부 프록시) 또는 `B` (내부 에이전트) |
| `token` | ✅ | 인증 토큰 | 서버에 설정된 액세스 토큰 |

### 연결 상태

- **연결 성공**: WebSocket이 열리고 메시지 송수신 가능
- **연결 실패**: 
  - 토큰이 잘못된 경우: `CloseStatus.POLICY_VIOLATION` (1008)
  - 네트워크 오류: 일반 WebSocket 오류 코드

---

## 🔐 인증

### 인증 방법

서버는 두 가지 방식으로 토큰을 받을 수 있습니다:

#### 1. Query Parameter (권장)

```
ws://server:8080/relay?type=A&token=YOUR_TOKEN
```

#### 2. Authorization Header

```
Authorization: Bearer YOUR_TOKEN
```

### 토큰 설정

현재 개발 환경에서는 하드코딩된 토큰을 사용합니다:

```
default-token-change-in-production
```

**⚠️ 경고**: 프로덕션 환경에서는 반드시 강력한 토큰으로 변경하세요!

### 인증 실패 시

- **응답**: WebSocket 연결이 즉시 종료됨
- **Close Code**: `1008` (Policy Violation)
- **Close Reason**: `"Invalid token"`

---

## 📨 메시지 포맷

모든 메시지는 **JSON 형식**으로 주고받습니다.

### 기본 메시지 구조

```json
{
  "type": "REQUEST|RESPONSE|PING|PONG",
  "sessionId": "string (optional)",
  "method": "GET|POST|PUT|DELETE|... (REQUEST일 때만)",
  "url": "string (REQUEST일 때만)",
  "headers": { "key": "value" },
  "body": "string (optional)",
  "statusCode": 200 (RESPONSE일 때만),
  "error": "string (에러 발생 시)"
}
```

### 메시지 타입

| 타입 | 설명 | 사용 시점 |
|------|------|----------|
| `REQUEST` | HTTP 요청 | Client A → Relay → Client B |
| `RESPONSE` | HTTP 응답 | Client B → Relay → Client A |
| `PING` | 연결 유지 요청 | 주기적으로 전송 |
| `PONG` | 연결 유지 응답 | PING에 대한 응답 |

---

### REQUEST 메시지 (Client A → Client B)

**용도**: 외부 프록시가 내부 에이전트에게 HTTP 요청을 전달

```json
{
  "type": "REQUEST",
  "sessionId": "unique-session-id-12345",
  "method": "GET",
  "url": "http://internal-api.company.com/api/data",
  "headers": {
    "Content-Type": "application/json",
    "Accept": "application/json",
    "User-Agent": "Mozilla/5.0..."
  },
  "body": null
}
```

#### 필수 필드

- `type`: 반드시 `"REQUEST"`여야 함
- `method`: HTTP 메서드 (GET, POST, PUT, DELETE, PATCH 등)
- `url`: 내부망의 전체 URL (프로토콜 포함)

#### 선택 필드

- `sessionId`: 요청을 추적하기 위한 고유 ID (권장)
- `headers`: HTTP 헤더 맵
- `body`: 요청 본문 (POST, PUT 등에서 사용)

#### 예시

```json
// HTTP GET 요청
{
  "type": "REQUEST",
  "method": "GET",
  "url": "http://internal-api.company.com/api/users/123"
}

// HTTPS GET 요청 (CONNECT 사용 안 함)
{
  "type": "REQUEST",
  "method": "GET",
  "url": "https://internal-api.company.com/api/users/123"
}

// HTTPS POST 요청
{
  "type": "REQUEST",
  "method": "POST",
  "url": "https://internal-api.company.com/api/users",
  "headers": {
    "Content-Type": "application/json"
  },
  "body": "{\"name\":\"John\",\"email\":\"john@example.com\"}"
}
```

**중요**: HTTPS 요청도 HTTP 요청과 동일하게 처리합니다. `url` 필드에 `https://` 프로토콜이 포함되어 있으면 Client B가 자동으로 HTTPS 요청을 수행합니다.

---

### RESPONSE 메시지 (Client B → Client A)

**용도**: 내부 에이전트가 HTTP 응답을 외부 프록시로 전달

```json
{
  "type": "RESPONSE",
  "sessionId": "unique-session-id-12345",
  "statusCode": 200,
  "headers": {
    "Content-Type": "application/json",
    "Content-Length": "1234"
  },
  "body": "{\"data\":\"response body\"}"
}
```

#### 필수 필드

- `type`: 반드시 `"RESPONSE"`여야 함
- `statusCode`: HTTP 상태 코드 (200, 404, 500 등)

#### 선택 필드

- `sessionId`: 요청의 sessionId와 동일한 값 (매칭용)
- `headers`: HTTP 응답 헤더 맵
- `body`: 응답 본문
- `error`: 에러 발생 시 에러 메시지

#### 예시

```json
// 성공 응답
{
  "type": "RESPONSE",
  "sessionId": "unique-session-id-12345",
  "statusCode": 200,
  "headers": {
    "Content-Type": "application/json"
  },
  "body": "{\"success\":true}"
}

// 에러 응답
{
  "type": "RESPONSE",
  "sessionId": "unique-session-id-12345",
  "statusCode": 500,
  "error": "Internal server error: Connection timeout"
}
```

---

### PING/PONG 메시지

**용도**: WebSocket 연결 유지 (Keep-Alive)

#### PING (클라이언트 → 서버)

```json
{
  "type": "PING"
}
```

#### PONG (서버 → 클라이언트)

```json
{
  "type": "PONG"
}
```

**권장**: 30초마다 PING을 전송하여 연결을 유지하세요.

---

## 💻 Client A 개발 가이드

**Client A**는 외부 개발 PC에서 실행되는 프록시입니다.

### 역할

1. 브라우저/개발툴의 HTTP 요청을 가로채기
2. 요청을 JSON으로 변환하여 Relay Server로 전송
3. Relay Server로부터 응답을 받아 원본 클라이언트로 반환

### 개발 요구사항

- **언어**: C# (.NET 6+ 또는 .NET Framework 4.8+)
- **기능**: HTTP 프록시 서버, WebSocket 클라이언트
- **형태**: Windows Service 또는 Tray Application

### 구현 단계

#### 1. WebSocket 연결

```csharp
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

public class ProxyClientA
{
    private ClientWebSocket _webSocket;
    private const string RELAY_URL = "ws://relay.example.com:8080/relay?type=A&token=default-token-change-in-production";
    
    public async Task ConnectAsync()
    {
        _webSocket = new ClientWebSocket();
        await _webSocket.ConnectAsync(new Uri(RELAY_URL), CancellationToken.None);
        
        // 메시지 수신 루프 시작
        _ = Task.Run(ReceiveMessagesAsync);
    }
}
```

#### 2. HTTP 요청 가로채기

```csharp
// HttpListener 또는 Titanium.Web.Proxy 사용
public void StartProxy(int port)
{
    var listener = new HttpListener();
    listener.Prefixes.Add($"http://localhost:{port}/");
    listener.Start();
    
    _ = Task.Run(async () =>
    {
        while (true)
        {
            var context = await listener.GetContextAsync();
            _ = Task.Run(() => HandleRequestAsync(context));
        }
    });
}
```

#### 3. 요청을 JSON으로 변환 및 전송

```csharp
private async Task HandleRequestAsync(HttpListenerContext context)
{
    var request = context.Request;
    
    // HTTPS CONNECT 요청 처리 (브라우저가 보낼 수 있음)
    if (request.HttpMethod == "CONNECT")
    {
        // CONNECT는 사용하지 않으므로 에러 반환
        // 또는 URL을 추출하여 일반 REQUEST로 변환
        var targetHost = request.Headers["Host"];
        if (!string.IsNullOrEmpty(targetHost))
        {
            // CONNECT 요청을 일반 HTTPS GET 요청으로 변환
            // (실제로는 브라우저가 보낸 원래 요청을 사용해야 함)
            await context.Response.CloseAsync();
            return;
        }
    }
    
    // 전체 URL 구성 (프로토콜 포함)
    string fullUrl;
    if (request.Url.IsAbsoluteUri)
    {
        fullUrl = request.Url.ToString();
    }
    else
    {
        // 상대 URL인 경우 절대 URL로 변환
        var scheme = request.IsSecureConnection ? "https" : "http";
        fullUrl = $"{scheme}://{request.Headers["Host"]}{request.Url}";
    }
    
    // RelayMessage 생성
    var relayMessage = new RelayMessage
    {
        Type = "REQUEST",  // 항상 REQUEST (CONNECT 사용 안 함)
        SessionId = Guid.NewGuid().ToString(),
        Method = request.HttpMethod,  // GET, POST, PUT, DELETE 등
        Url = fullUrl,  // https:// 또는 http:// 포함한 전체 URL
        Headers = request.Headers.AllKeys.ToDictionary(
            k => k, 
            k => request.Headers[k]
        ),
        Body = await ReadRequestBodyAsync(request)
    };
    
    // JSON 변환 및 전송
    var json = JsonSerializer.Serialize(relayMessage);
    var bytes = Encoding.UTF8.GetBytes(json);
    await _webSocket.SendAsync(
        new ArraySegment<byte>(bytes),
        WebSocketMessageType.Text,
        true,
        CancellationToken.None
    );
    
    // 응답 대기 (세션 ID로 매칭)
    // ...
}
```

#### 4. 응답 수신 및 반환

```csharp
private async Task ReceiveMessagesAsync()
{
    var buffer = new byte[4096];
    
    while (_webSocket.State == WebSocketState.Open)
    {
        var result = await _webSocket.ReceiveAsync(
            new ArraySegment<byte>(buffer),
            CancellationToken.None
        );
        
        if (result.MessageType == WebSocketMessageType.Text)
        {
            var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
            var response = JsonSerializer.Deserialize<RelayMessage>(json);
            
            // sessionId로 매칭하여 원본 요청에 응답 반환
            await SendResponseToClientAsync(response);
        }
    }
}
```

#### 5. PING 전송 (연결 유지)

```csharp
private async Task SendPingAsync()
{
    while (_webSocket.State == WebSocketState.Open)
    {
        await Task.Delay(30000); // 30초마다
        
        var ping = new RelayMessage { Type = "PING" };
        var json = JsonSerializer.Serialize(ping);
        var bytes = Encoding.UTF8.GetBytes(json);
        
        await _webSocket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            true,
            CancellationToken.None
        );
    }
}
```

### HTTPS 요청 처리

**중요**: 이 시스템은 **CONNECT 메서드를 사용하지 않습니다**. HTTPS 요청도 일반 HTTP 요청처럼 처리합니다.

#### 처리 방식

1. **브라우저의 HTTPS 요청 수신**
   - 브라우저가 `https://internal-server.com/api/data` 요청
   - Client A가 프록시로 요청을 받음

2. **일반 REQUEST 메시지로 변환**
   - URL에서 프로토콜(`https://`)과 전체 경로 추출
   - `method`는 원래 HTTP 메서드 사용 (GET, POST 등)
   - `type`은 항상 `"REQUEST"`로 설정

3. **예시**

```json
// 브라우저: GET https://internal-api.company.com/api/users
// Client A가 변환한 메시지:
{
  "type": "REQUEST",
  "sessionId": "unique-id-123",
  "method": "GET",
  "url": "https://internal-api.company.com/api/users",
  "headers": {
    "User-Agent": "Mozilla/5.0...",
    "Accept": "application/json"
  }
}
```

4. **Client B에서 HTTPS 요청 수행**
   - Client B가 받은 URL(`https://...`)로 직접 HTTPS 요청 수행
   - 내부 서버의 SSL 인증서 검증 (필요시 인증서 신뢰 설정)

#### CONNECT 메서드 사용 금지

- ❌ `type: "CONNECT"` 메시지 전송 금지
- ❌ `method: "CONNECT"` 사용 금지
- ✅ HTTPS URL을 그대로 사용하여 일반 REQUEST로 전송

### 주요 고려사항

1. **HTTPS 요청 처리**: CONNECT 없이 일반 REQUEST로 변환하여 전송
2. **세션 관리**: sessionId로 요청-응답 매칭
3. **타임아웃**: 장시간 응답 대기 시 타임아웃 처리
4. **재연결**: 연결 끊김 시 자동 재연결 (선택사항)
5. **SSL 인증서**: Client B에서 내부 서버의 SSL 인증서를 신뢰하도록 설정 필요

---

## 🤖 Client B 개발 가이드

**Client B**는 내부 작업 PC에서 실행되는 에이전트입니다.

### 역할

1. Relay Server와 Outbound WebSocket 연결 유지
2. Relay Server로부터 요청을 받아 내부망 서버로 HTTP 요청 수행
3. 응답을 JSON으로 변환하여 Relay Server로 전송

### 개발 요구사항

- **언어**: C# (.NET 6+ 또는 .NET Framework 4.8+)
- **기능**: WebSocket 클라이언트, HTTP 클라이언트
- **형태**: Windows Service 또는 Tray Application
- **재연결**: 필수 (네트워크 끊김, PC Sleep/Wake 대응)

### 구현 단계

#### 1. WebSocket 연결 및 재연결 로직

```csharp
public class AgentClientB
{
    private ClientWebSocket _webSocket;
    private const string RELAY_URL = "ws://relay.example.com:8080/relay?type=B&token=default-token-change-in-production";
    
    public async Task StartAsync()
    {
        while (true)
        {
            try
            {
                await ConnectAndListenAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Connection error: {ex.Message}");
                await Task.Delay(3000); // 3초 후 재시도
            }
        }
    }
    
    private async Task ConnectAndListenAsync()
    {
        _webSocket = new ClientWebSocket();
        await _webSocket.ConnectAsync(new Uri(RELAY_URL), CancellationToken.None);
        
        Console.WriteLine("Connected to Relay Server");
        
        // 메시지 수신 루프
        await ReceiveMessagesAsync();
    }
}
```

#### 2. 메시지 수신 및 처리

```csharp
private async Task ReceiveMessagesAsync()
{
    var buffer = new byte[4096];
    
    while (_webSocket.State == WebSocketState.Open)
    {
        var result = await _webSocket.ReceiveAsync(
            new ArraySegment<byte>(buffer),
            CancellationToken.None
        );
        
        if (result.MessageType == WebSocketMessageType.Text)
        {
            var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
            var message = JsonSerializer.Deserialize<RelayMessage>(json);
            
            if (message.Type == "REQUEST")
            {
                await HandleRequestAsync(message);
            }
            else if (message.Type == "PING")
            {
                await SendPongAsync();
            }
        }
    }
}
```

#### 3. 내부망 HTTP/HTTPS 요청 수행

```csharp
private async Task HandleRequestAsync(RelayMessage request)
{
    try
    {
        using var httpClient = new HttpClient();
        
        // HTTPS 요청을 위한 SSL 인증서 검증 설정 (필요시)
        // 내부 서버의 자체 서명 인증서를 사용하는 경우:
        // ServicePointManager.ServerCertificateValidationCallback = 
        //     (sender, cert, chain, errors) => true;  // 개발 환경만
        
        // 헤더 설정
        foreach (var header in request.Headers ?? new Dictionary<string, string>())
        {
            // 일부 헤더는 HttpClient에서 자동 처리되므로 제외
            if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) ||
                header.Key.Equals("Accept", StringComparison.OrdinalIgnoreCase))
            {
                httpClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
            }
        }
        
        // HTTP/HTTPS 요청 생성
        // request.Url은 이미 https:// 또는 http:// 포함
        HttpRequestMessage httpRequest = new HttpRequestMessage(
            new HttpMethod(request.Method),  // GET, POST, PUT, DELETE 등
            request.Url  // https://internal-api.company.com/api/data
        );
        
        // 요청 본문 설정
        if (!string.IsNullOrEmpty(request.Body))
        {
            // Content-Type 헤더 확인
            var contentType = request.Headers?.ContainsKey("Content-Type") == true
                ? request.Headers["Content-Type"]
                : "application/json";
            
            httpRequest.Content = new StringContent(
                request.Body,
                Encoding.UTF8,
                contentType
            );
        }
        
        // 내부망 서버로 요청 (HTTP 또는 HTTPS)
        var response = await httpClient.SendAsync(httpRequest);
        
        // 응답 헤더 수집
        var responseHeaders = new Dictionary<string, string>();
        foreach (var header in response.Headers)
        {
            responseHeaders[header.Key] = string.Join(", ", header.Value);
        }
        foreach (var header in response.Content.Headers)
        {
            responseHeaders[header.Key] = string.Join(", ", header.Value);
        }
        
        // 응답 생성
        var relayResponse = new RelayMessage
        {
            Type = "RESPONSE",
            SessionId = request.SessionId,
            StatusCode = (int)response.StatusCode,
            Headers = responseHeaders,
            Body = await response.Content.ReadAsStringAsync()
        };
        
        // Relay Server로 응답 전송
        await SendMessageAsync(relayResponse);
    }
    catch (Exception ex)
    {
        // 에러 응답 전송
        var errorResponse = new RelayMessage
        {
            Type = "RESPONSE",
            SessionId = request.SessionId,
            StatusCode = 500,
            Error = $"Request failed: {ex.Message}"
        };
        
        await SendMessageAsync(errorResponse);
    }
}
```

#### 4. 메시지 전송

```csharp
private async Task SendMessageAsync(RelayMessage message)
{
    var json = JsonSerializer.Serialize(message);
    var bytes = Encoding.UTF8.GetBytes(json);
    
    await _webSocket.SendAsync(
        new ArraySegment<byte>(bytes),
        WebSocketMessageType.Text,
        true,
        CancellationToken.None
    );
}

private async Task SendPongAsync()
{
    var pong = new RelayMessage { Type = "PONG" };
    await SendMessageAsync(pong);
}
```

### 주요 고려사항

1. **자동 재연결**: 필수 구현
   - 네트워크 끊김
   - PC Sleep/Wake
   - VPN 연결 변경
   - Relay 서버 재시작

2. **도메인 화이트리스트**: 보안을 위해 허용된 도메인만 요청

```csharp
private readonly HashSet<string> _allowedDomains = new()
{
    "internal-api.company.com",
    "internal-service.company.com"
};

private bool IsAllowedDomain(string url)
{
    var uri = new Uri(url);
    return _allowedDomains.Contains(uri.Host);
}
```

3. **백그라운드 실행**: Windows Service로 구현 권장

---

## ⚠️ 에러 처리

### 일반적인 에러 시나리오

#### 1. 인증 실패

```json
// WebSocket 연결이 즉시 종료됨
// Close Code: 1008
// Close Reason: "Invalid token"
```

**대응**: 토큰 확인 및 재연결

#### 2. 매핑된 상대방 없음

```json
// Client A가 요청을 보냈지만 Client B가 없을 때
{
  "type": "RESPONSE",
  "statusCode": 500,
  "error": "No active agent available"
}
```

**대응**: Client B 연결 상태 확인

#### 3. 내부망 요청 실패

```json
// Client B가 내부망 서버 요청 실패 시
{
  "type": "RESPONSE",
  "sessionId": "original-session-id",
  "statusCode": 500,
  "error": "Connection timeout"
}
```

**대응**: 내부망 서버 상태 확인

#### 4. 잘못된 메시지 포맷

```json
// 서버가 메시지를 파싱하지 못할 때
// 메시지는 무시되고 로그만 기록됨
```

**대응**: JSON 형식 확인

---

## 📝 예제 코드

### C# 전체 예제 (Client B)

```csharp
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ProxyRelayClient
{
    public class RelayMessage
    {
        public string Type { get; set; }
        public string SessionId { get; set; }
        public string Method { get; set; }
        public string Url { get; set; }
        public Dictionary<string, string> Headers { get; set; }
        public string Body { get; set; }
        public int? StatusCode { get; set; }
        public string Error { get; set; }
    }

    public class AgentClientB
    {
        private ClientWebSocket _webSocket;
        private readonly string _relayUrl;
        private readonly HttpClient _httpClient;

        public AgentClientB(string relayUrl)
        {
            _relayUrl = relayUrl;
            _httpClient = new HttpClient();
        }

        public async Task StartAsync()
        {
            while (true)
            {
                try
                {
                    await ConnectAndListenAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}. Retrying in 3 seconds...");
                    await Task.Delay(3000);
                }
            }
        }

        private async Task ConnectAndListenAsync()
        {
            _webSocket = new ClientWebSocket();
            await _webSocket.ConnectAsync(new Uri(_relayUrl), CancellationToken.None);
            Console.WriteLine("Connected to Relay Server");

            var buffer = new byte[4096];

            while (_webSocket.State == WebSocketState.Open)
            {
                var result = await _webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    CancellationToken.None
                );

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    var message = JsonSerializer.Deserialize<RelayMessage>(json);

                    if (message.Type == "REQUEST")
                    {
                        _ = Task.Run(() => HandleRequestAsync(message));
                    }
                    else if (message.Type == "PING")
                    {
                        await SendPongAsync();
                    }
                }
            }
        }

        private async Task HandleRequestAsync(RelayMessage request)
        {
            try
            {
                var httpRequest = new HttpRequestMessage(
                    new HttpMethod(request.Method),
                    request.Url
                );

                if (!string.IsNullOrEmpty(request.Body))
                {
                    httpRequest.Content = new StringContent(
                        request.Body,
                        Encoding.UTF8,
                        "application/json"
                    );
                }

                var response = await _httpClient.SendAsync(httpRequest);
                var responseBody = await response.Content.ReadAsStringAsync();

                var relayResponse = new RelayMessage
                {
                    Type = "RESPONSE",
                    SessionId = request.SessionId,
                    StatusCode = (int)response.StatusCode,
                    Body = responseBody
                };

                await SendMessageAsync(relayResponse);
            }
            catch (Exception ex)
            {
                var errorResponse = new RelayMessage
                {
                    Type = "RESPONSE",
                    SessionId = request.SessionId,
                    StatusCode = 500,
                    Error = ex.Message
                };

                await SendMessageAsync(errorResponse);
            }
        }

        private async Task SendMessageAsync(RelayMessage message)
        {
            var json = JsonSerializer.Serialize(message);
            var bytes = Encoding.UTF8.GetBytes(json);

            await _webSocket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                CancellationToken.None
            );
        }

        private async Task SendPongAsync()
        {
            var pong = new RelayMessage { Type = "PONG" };
            await SendMessageAsync(pong);
        }
    }

    class Program
    {
        static async Task Main(string[] args)
        {
            var relayUrl = "ws://localhost:8080/relay?type=B&token=default-token-change-in-production";
            var agent = new AgentClientB(relayUrl);
            await agent.StartAsync();
        }
    }
}
```

---

## ❓ FAQ

### Q1. WebSocket 연결이 자주 끊어집니다.

**A**: 다음을 확인하세요:
- PING을 30초마다 전송하고 있는지
- 네트워크 방화벽이 WebSocket을 차단하지 않는지
- 서버의 타임아웃 설정 확인

### Q2. Client A와 Client B가 매핑되지 않습니다.

**A**: 
- Client B가 먼저 연결되어야 합니다
- Client A가 연결되면 자동으로 매핑됩니다
- 매핑은 1:1 관계입니다

### Q3. 내부망 서버로 요청이 전달되지 않습니다.

**A**: 
- Client B가 정상적으로 연결되어 있는지 확인
- 내부망 서버 URL이 올바른지 확인
- Client B의 도메인 화이트리스트 확인

### Q4. HTTPS 요청은 어떻게 처리하나요?

**A**: 
- **CONNECT 메서드를 사용하지 않습니다**
- HTTPS 요청도 일반 HTTP 요청처럼 처리합니다:
  1. Client A가 브라우저의 HTTPS 요청을 받음
  2. URL을 추출하여 (`https://internal-server.com/api/data`) 일반 REQUEST 메시지로 변환
  3. `type: "REQUEST"`, `method: "GET"` (또는 POST 등), `url: "https://..."` 형태로 전송
  4. Client B가 받은 URL로 직접 HTTPS 요청 수행
  5. 응답을 JSON으로 변환하여 전달

**예시**:
```json
// Client A가 보내는 메시지
{
  "type": "REQUEST",
  "method": "GET",
  "url": "https://internal-api.company.com/api/data"
}
```

**주의사항**:
- Client B에서 내부 서버의 SSL 인증서를 신뢰하도록 설정 필요 (자체 서명 인증서 사용 시)
- 프로덕션 환경에서는 적절한 인증서 검증 로직 구현 권장

### Q5. 여러 Client A가 동시에 연결할 수 있나요?

**A**: 
- 가능합니다. 각 Client A는 별도의 Client B와 매핑됩니다
- Client B가 부족하면 에러 응답이 반환됩니다

---

## 📞 지원

문제가 발생하거나 질문이 있으시면 개발팀에 문의하세요.

---

**문서 버전**: 1.0  
**최종 업데이트**: 2024

