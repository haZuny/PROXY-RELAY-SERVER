using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net.Http;
using System.Net.Security;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace ClientInternalPC
{
    /// <summary>
    /// Client B: 내부망에 설치되는 에이전트
    /// Relay Server와 WebSocket 연결을 유지하고, 내부망 HTTP 요청을 수행합니다.
    /// </summary>
    public class AgentClientB
    {
        private ClientWebSocket _webSocket;
        private readonly string _relayUrl;
        private readonly HttpClient _httpClient;
        private readonly HashSet<string> _allowedDomains;
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isRunning;
        private readonly object _lockObject = new object();

        // 이벤트: 연결 상태 변경, 로그 메시지
        public event EventHandler<bool> ConnectionStatusChanged;
        public event EventHandler<string> LogMessage;

        public AgentClientB()
        {
            // App.config에서 설정 읽기
            var relayHost = ConfigurationManager.AppSettings["RelayHost"] ?? "localhost";
            var relayPort = ConfigurationManager.AppSettings["RelayPort"] ?? "8080";
            var relayToken = ConfigurationManager.AppSettings["RelayToken"] ?? "default-token-change-in-production";
            var useSecure = ConfigurationManager.AppSettings["UseSecure"] ?? "false";

            var protocol = useSecure.Equals("true", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws";
            _relayUrl = $"{protocol}://{relayHost}:{relayPort}/relay?type=B&token={relayToken}";

            // SSL 인증서 검증 우회 설정 (내부망 자체 서명 인증서용)
            var ignoreSslErrors = ConfigurationManager.AppSettings["IgnoreSslErrors"] ?? "true";
            if (ignoreSslErrors.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                // .NET Framework 4.5.2에서는 ServicePointManager 사용
                // 전역적으로 모든 SSL 인증서 허용
                System.Net.ServicePointManager.ServerCertificateValidationCallback = 
                    delegate (object s, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
                    {
                        return true; // 모든 인증서 허용
                    };
                
                // SSL 프로토콜 버전 설정 (TLS 1.2 이상)
                System.Net.ServicePointManager.SecurityProtocol = 
                    System.Net.SecurityProtocolType.Tls12 | 
                    System.Net.SecurityProtocolType.Tls11 | 
                    System.Net.SecurityProtocolType.Tls;
            }

            // HttpClientHandler 설정
            var httpClientHandler = new HttpClientHandler();
            
            // 자동 리다이렉트 허용 (기본값: true)
            httpClientHandler.AllowAutoRedirect = true;
            
            // 최대 리다이렉트 횟수 설정
            httpClientHandler.MaxAutomaticRedirections = 10;

            _httpClient = new HttpClient(httpClientHandler);
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            
            // User-Agent 설정 (일부 서버가 User-Agent 없으면 차단)
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "ClientB-Agent/1.0");

            // 허용된 도메인 목록 (설정에서 읽거나 기본값)
            var allowedDomainsConfig = ConfigurationManager.AppSettings["AllowedDomains"];
            if (!string.IsNullOrEmpty(allowedDomainsConfig))
            {
                _allowedDomains = new HashSet<string>(
                    allowedDomainsConfig.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(d => d.Trim()),
                    StringComparer.OrdinalIgnoreCase
                );
            }
            else
            {
                // 기본값: 모든 내부망 도메인 허용 (보안상 프로덕션에서는 제한 필요)
                _allowedDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// 에이전트 시작 (자동 재연결 포함)
        /// </summary>
        public async Task StartAsync()
        {
            if (_isRunning)
            {
                Log("에이전트가 이미 실행 중입니다.");
                return;
            }

            _isRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();

            Log($"에이전트 시작 - Relay Server: {_relayUrl}");

            while (_isRunning && !_cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    await ConnectAndListenAsync(_cancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    Log("에이전트가 중지되었습니다.");
                    break;
                }
                catch (Exception ex)
                {
                    Log($"연결 오류: {ex.Message}. 3초 후 재시도...");
                    OnConnectionStatusChanged(false);
                    await Task.Delay(3000, _cancellationTokenSource.Token);
                }
            }
        }

        /// <summary>
        /// 에이전트 중지
        /// </summary>
        public async Task StopAsync()
        {
            _isRunning = false;
            _cancellationTokenSource?.Cancel();

            if (_webSocket != null && _webSocket.State == WebSocketState.Open)
            {
                try
                {
                    await _webSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Agent stopping",
                        CancellationToken.None
                    );
                }
                catch { }
            }

            _webSocket?.Dispose();
            _webSocket = null;

            Log("에이전트가 중지되었습니다.");
        }

        /// <summary>
        /// WebSocket 연결 및 메시지 수신 루프
        /// </summary>
        private async Task ConnectAndListenAsync(CancellationToken cancellationToken)
        {
            _webSocket?.Dispose();
            _webSocket = new ClientWebSocket();

            try
            {
                await _webSocket.ConnectAsync(new Uri(_relayUrl), cancellationToken);
                Log("Relay Server에 연결되었습니다.");
                OnConnectionStatusChanged(true);

                // PING 전송 태스크 시작
                var pingTask = Task.Run(() => SendPingLoopAsync(cancellationToken), cancellationToken);

                // 메시지 수신 루프
                var buffer = new byte[4096];
                while (_webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    var result = await _webSocket.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        cancellationToken
                    );

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Log($"연결이 종료되었습니다: {result.CloseStatus} - {result.CloseStatusDescription}");
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        Log($"[수신 루프] Text 메시지 수신 (길이: {result.Count}): {json?.Substring(0, Math.Min(50, json?.Length ?? 0))}...");
                        await HandleMessageAsync(json);
                    }
                    else if (result.MessageType == WebSocketMessageType.Binary)
                    {
                        Log($"[수신 루프] Binary 메시지 수신 (길이: {result.Count}) - 무시");
                    }
                }
            }
            finally
            {
                OnConnectionStatusChanged(false);
            }
        }

        /// <summary>
        /// 수신한 메시지 처리
        /// </summary>
        private async Task HandleMessageAsync(string json)
        {
            // 디버깅: 메시지 수신 확인 (항상 첫 줄에 출력)
            Log("=== HandleMessageAsync 호출됨 ===");
            var jsonPreview = json?.Length > 100 ? json.Substring(0, 100) + "..." : json;
            Log($"메시지 수신됨 (길이: {json?.Length ?? 0}): {jsonPreview}");
            
            try
            {
                var settings = new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore
                };
                var message = JsonConvert.DeserializeObject<RelayMessage>(json, settings);

                if (message == null || string.IsNullOrEmpty(message.Type))
                {
                    Log($"잘못된 메시지 포맷: {json}");
                    return;
                }

                Log($"메시지 파싱 성공: Type={message.Type}, Method={message.Method ?? "null"}, Url={message.Url ?? "null"}, SessionId={message.SessionId ?? "null"}");

                switch (message.Type.ToUpper())
                {
                    case "REQUEST":
                        Log($"REQUEST 메시지 처리 시작: {message.Method} {message.Url}");
                        // 예외를 잡아서 로그 남기기
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await HandleRequestAsync(message);
                            }
                            catch (Exception ex)
                            {
                                Log($"HandleRequestAsync 예외: {ex.GetType().Name} - {ex.Message}");
                                if (ex.InnerException != null)
                                {
                                    Log($"  내부: {ex.InnerException.GetType().Name} - {ex.InnerException.Message}");
                                }
                                // 에러 응답 전송
                                await SendErrorResponseAsync(message.SessionId, 500, $"Request processing failed: {ex.Message}");
                            }
                        });
                        break;

                    case "PING":
                        // 서버로부터 PING을 받으면 PONG 응답
                        await SendPongAsync();
                        break;

                    case "PONG":
                        // 서버로부터 PONG을 받음 (클라이언트가 보낸 PING에 대한 응답)
                        // 연결이 정상적으로 유지되고 있음을 확인
                        // 로그는 필요시에만 남김 (너무 많이 남기지 않음)
                        break;

                    default:
                        Log($"알 수 없는 메시지 타입: {message.Type}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Log($"메시지 처리 오류: {ex.Message}");
            }
        }

        /// <summary>
        /// HTTP 요청 처리
        /// </summary>
        private async Task HandleRequestAsync(RelayMessage request)
        {
            try
            {
                // 도메인 화이트리스트 확인
                if (_allowedDomains.Count > 0 && !string.IsNullOrEmpty(request.Url))
                {
                    var uri = new Uri(request.Url);
                    if (!_allowedDomains.Contains(uri.Host))
                    {
                        Log($"허용되지 않은 도메인: {uri.Host}");
                        await SendErrorResponseAsync(request.SessionId, 403, $"Domain not allowed: {uri.Host}");
                        return;
                    }
                }

                Log($"요청 처리: {request.Method} {request.Url}");

                // HTTP 요청 생성
                var httpRequest = new HttpRequestMessage(
                    new HttpMethod(request.Method),
                    request.Url
                );

                // 헤더 설정
                if (request.Headers != null)
                {
                    foreach (var header in request.Headers)
                    {
                        try
                        {
                            // 일부 헤더는 HttpClient에서 자동 처리하므로 제외
                            if (header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
                                header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                                header.Key.Equals("Connection", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                            {
                                // Content-Type은 Content에 설정
                                continue;
                            }

                            httpRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
                        }
                        catch
                        {
                            // 헤더 추가 실패 시 무시
                        }
                    }
                }

                // Body 설정
                if (!string.IsNullOrEmpty(request.Body) &&
                    (request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase) ||
                     request.Method.Equals("PUT", StringComparison.OrdinalIgnoreCase) ||
                     request.Method.Equals("PATCH", StringComparison.OrdinalIgnoreCase)))
                {
                    var contentType = "application/json";
                    if (request.Headers != null && request.Headers.ContainsKey("Content-Type"))
                    {
                        contentType = request.Headers["Content-Type"];
                    }

                    httpRequest.Content = new StringContent(request.Body, Encoding.UTF8, contentType);
                }

                // 내부망 서버로 요청
                var response = await _httpClient.SendAsync(httpRequest);

                // 응답 본문 읽기
                var responseBody = await response.Content.ReadAsStringAsync();

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

                // Relay Server로 응답 전송
                var relayResponse = new RelayMessage
                {
                    Type = "RESPONSE",
                    SessionId = request.SessionId,
                    StatusCode = (int)response.StatusCode,
                    Headers = responseHeaders,
                    Body = responseBody
                };

                await SendMessageAsync(relayResponse);
                Log($"응답 전송: {response.StatusCode} - {request.Url}");
            }
            catch (Exception ex)
            {
                // 더 자세한 에러 정보 로깅
                var errorDetails = $"요청 처리 오류 [{request.Method} {request.Url}]: {ex.GetType().Name} - {ex.Message}";
                
                if (ex.InnerException != null)
                {
                    errorDetails += $"\n  내부 오류: {ex.InnerException.GetType().Name} - {ex.InnerException.Message}";
                }
                
                // HttpRequestException의 경우 추가 정보
                if (ex is HttpRequestException httpEx)
                {
                    errorDetails += $"\n  HTTP 요청 실패";
                    if (httpEx.InnerException != null)
                    {
                        errorDetails += $"\n  HTTP 내부: {httpEx.InnerException.GetType().Name} - {httpEx.InnerException.Message}";
                    }
                }
                
                // TaskCanceledException (타임아웃)의 경우
                if (ex is TaskCanceledException)
                {
                    errorDetails += $"\n  타임아웃 발생 (30초 초과)";
                }
                
                // SocketException의 경우
                if (ex.InnerException is System.Net.Sockets.SocketException socketEx)
                {
                    errorDetails += $"\n  소켓 오류 코드: {socketEx.ErrorCode}, 소켓 오류: {socketEx.SocketErrorCode}";
                }
                
                // AggregateException의 경우 (여러 예외 포함)
                if (ex is AggregateException aggEx)
                {
                    errorDetails += $"\n  집계된 예외 {aggEx.InnerExceptions.Count}개:";
                    foreach (var innerEx in aggEx.InnerExceptions)
                    {
                        errorDetails += $"\n    - {innerEx.GetType().Name}: {innerEx.Message}";
                    }
                }
                
                Log(errorDetails);
                await SendErrorResponseAsync(request.SessionId, 500, $"Request failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 에러 응답 전송
        /// </summary>
        private async Task SendErrorResponseAsync(string sessionId, int statusCode, string error)
        {
            var errorResponse = new RelayMessage
            {
                Type = "RESPONSE",
                SessionId = sessionId,
                StatusCode = statusCode,
                Error = error
            };

            await SendMessageAsync(errorResponse);
        }

        /// <summary>
        /// 메시지 전송
        /// </summary>
        private async Task SendMessageAsync(RelayMessage message)
        {
            if (_webSocket == null || _webSocket.State != WebSocketState.Open)
            {
                Log("WebSocket이 연결되지 않아 메시지를 전송할 수 없습니다.");
                return;
            }

            // 메시지 유효성 검증
            if (message == null || string.IsNullOrWhiteSpace(message.Type))
            {
                Log("전송할 메시지가 유효하지 않습니다 (Type이 비어있음).");
                return;
            }

            try
            {
                var settings = new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore
                };
                var json = JsonConvert.SerializeObject(message, settings);
                
                // 직렬화 결과가 빈 JSON인지 확인
                if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}")
                {
                    Log($"직렬화된 메시지가 비어있습니다. Type: {message.Type}");
                    return;
                }
                
                var bytes = Encoding.UTF8.GetBytes(json);

                ClientWebSocket ws;
                lock (_lockObject)
                {
                    if (_webSocket == null || _webSocket.State != WebSocketState.Open)
                    {
                        return;
                    }
                    ws = _webSocket;
                }

                await ws.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None
                );
            }
            catch (Exception ex)
            {
                Log($"메시지 전송 오류: {ex.Message}");
            }
        }

        /// <summary>
        /// PONG 전송
        /// </summary>
        private async Task SendPongAsync()
        {
            var pong = new RelayMessage { Type = "PONG" };
            await SendMessageAsync(pong);
        }

        /// <summary>
        /// PING 전송 루프 (30초마다)
        /// </summary>
        private async Task SendPingLoopAsync(CancellationToken cancellationToken)
        {
            while (_webSocket != null && _webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(30000, cancellationToken); // 30초 대기

                    if (_webSocket.State == WebSocketState.Open)
                    {
                        var ping = new RelayMessage { Type = "PING" };
                        await SendMessageAsync(ping);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log($"PING 전송 오류: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 연결 상태 변경 이벤트 발생
        /// </summary>
        private void OnConnectionStatusChanged(bool isConnected)
        {
            ConnectionStatusChanged?.Invoke(this, isConnected);
        }

        /// <summary>
        /// 로그 메시지 이벤트 발생
        /// </summary>
        private void Log(string message)
        {
            var logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            LogMessage?.Invoke(this, logMessage);
            System.Diagnostics.Debug.WriteLine(logMessage);
        }
    }
}

