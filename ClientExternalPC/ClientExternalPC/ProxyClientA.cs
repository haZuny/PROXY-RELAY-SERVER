using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ClientExternalPC
{
    /// <summary>
    /// Client A: 외부 개발 PC에서 실행되는 프록시 클라이언트
    /// </summary>
    public class ProxyClientA : IDisposable
    {
        private ClientWebSocket _webSocket;
        private HttpListener _httpListener;
        private readonly string _relayServerUrl;  // http://localhost:8080
        private readonly int _proxyPort;
        private readonly string _domainFilter;
        private readonly string _accessToken;  // Relay Server에 설정된 토큰
        private readonly Dictionary<string, TaskCompletionSource<RelayMessage>> _pendingRequests;
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isRunning;
        private Task _receiveTask;
        private Task _pingTask;
        private HashSet<string> _allowedDomains;

        public event EventHandler<string> LogMessage;
        public event EventHandler<bool> ConnectionStatusChanged;

        public bool IsConnected => _webSocket?.State == WebSocketState.Open;
        public bool IsProxyRunning => _isRunning;

        public ProxyClientA(string relayServerUrl, int proxyPort = 8888, string domainFilter = "", string accessToken = "")
        {
            _relayServerUrl = relayServerUrl ?? "http://localhost:8080";
            _proxyPort = proxyPort;
            _domainFilter = domainFilter ?? "";
            _accessToken = accessToken ?? "default-token-change-in-production";
            _pendingRequests = new Dictionary<string, TaskCompletionSource<RelayMessage>>();
            
            // 도메인 필터 파싱
            if (!string.IsNullOrWhiteSpace(_domainFilter))
            {
                _allowedDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var domains = _domainFilter.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var domain in domains)
                {
                    var trimmed = domain.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                    {
                        _allowedDomains.Add(trimmed);
                    }
                }
            }
        }

        /// <summary>
        /// Relay Server에 연결하고 프록시 서버 시작
        /// </summary>
        public async Task StartAsync()
        {
            if (_isRunning)
                return;

            _cancellationTokenSource = new CancellationTokenSource();
            _isRunning = true;

            try
            {
                // WebSocket 연결
                await ConnectToRelayAsync();

                // HTTP 프록시 서버 시작
                StartProxyServer();

                // 메시지 수신 루프 시작
                _receiveTask = Task.Run(() => ReceiveMessagesAsync(_cancellationTokenSource.Token));

                // PING 전송 루프 시작
                _pingTask = Task.Run(() => SendPingLoopAsync(_cancellationTokenSource.Token));

                OnLogMessage("[시스템] 프록시 서버가 시작되었습니다.");
            }
            catch (Exception ex)
            {
                _isRunning = false;
                OnLogMessage($"[시스템 오류] 시작 실패: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Relay Server에 WebSocket 연결
        /// </summary>
        private async Task ConnectToRelayAsync()
        {
            try
            {
                // WebSocket URL 생성 (ws:// 또는 wss://)
                var serverUri = new Uri(_relayServerUrl);
                var wsScheme = serverUri.Scheme == "https" ? "wss" : "ws";
                var wsUrl = $"{wsScheme}://{serverUri.Host}:{serverUri.Port}/relay?type=A&token={Uri.EscapeDataString(_accessToken)}";

                _webSocket = new ClientWebSocket();
                await _webSocket.ConnectAsync(new Uri(wsUrl), CancellationToken.None);
                OnConnectionStatusChanged(true);
                OnLogMessage($"[연결 성공] Relay Server에 연결되었습니다: {wsUrl.Replace(_accessToken, "***")}");
            }
            catch (Exception ex)
            {
                OnConnectionStatusChanged(false);
                OnLogMessage($"[연결 실패] Relay Server 연결 실패: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// HTTP 프록시 서버 시작
        /// </summary>
        private void StartProxyServer()
        {
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add($"http://localhost:{_proxyPort}/");
            _httpListener.Start();

            Task.Run(async () =>
            {
                OnLogMessage($"[프록시 시작] HTTP 프록시 서버가 시작되었습니다: http://localhost:{_proxyPort}");

                while (_isRunning && _httpListener.IsListening)
                {
                    try
                    {
                        var context = await _httpListener.GetContextAsync();
                        _ = Task.Run(() => HandleProxyRequestAsync(context));
                    }
                    catch (Exception ex)
                    {
                        if (_isRunning)
                        {
                            OnLogMessage($"[요청 오류] 프록시 요청 처리 오류: {ex.Message}");
                        }
                    }
                }
            });
        }

        /// <summary>
        /// 프록시 요청 처리
        /// </summary>
        private async Task HandleProxyRequestAsync(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                // 도메인 필터링 체크
                if (!ShouldProxyRequest(request.Url))
                {
                    response.StatusCode = 403;
                    response.StatusDescription = "Forbidden - Domain not in filter list";
                    response.Close();
                    OnLogMessage($"[필터링 차단] {request.HttpMethod} {request.Url} - 도메인 '{request.Url?.Host}'이(가) 필터 목록에 없습니다");
                    return;
                }
                
                OnLogMessage($"[필터링 허용] {request.HttpMethod} {request.Url} - 도메인 '{request.Url?.Host}'이(가) 필터를 통과했습니다");

                // CONNECT 메서드 처리 (HTTPS 터널링)
                if (request.HttpMethod == "CONNECT")
                {
                    await HandleConnectRequestAsync(context);
                    return;
                }

                // 일반 HTTP 요청 처리
                var sessionId = Guid.NewGuid().ToString();
                var relayMessage = new RelayMessage
                {
                    Type = "REQUEST",
                    SessionId = sessionId,
                    Method = request.HttpMethod,
                    Url = request.Url.ToString(),
                    Headers = new Dictionary<string, string>()
                };

                // 헤더 복사
                foreach (string key in request.Headers.AllKeys)
                {
                    if (!key.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase) &&
                        !key.Equals("Connection", StringComparison.OrdinalIgnoreCase) &&
                        !key.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase))
                    {
                        relayMessage.Headers[key] = request.Headers[key];
                    }
                }

                // 요청 본문 읽기
                if (request.HasEntityBody)
                {
                    using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                    {
                        relayMessage.Body = await reader.ReadToEndAsync();
                    }
                }

                // Relay Server로 전송
                OnLogMessage($"[요청 전송] {relayMessage.Method} {relayMessage.Url} (SessionId: {sessionId})");
                await SendMessageAsync(relayMessage);

                // 응답 대기
                var tcs = new TaskCompletionSource<RelayMessage>();
                _pendingRequests[sessionId] = tcs;

                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(60));
                var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    _pendingRequests.Remove(sessionId);
                    OnLogMessage($"[타임아웃] {relayMessage.Method} {relayMessage.Url} - 60초 내 응답 없음 (SessionId: {sessionId})");
                    response.StatusCode = 504; // Gateway Timeout
                    response.StatusDescription = "Gateway Timeout";
                    await response.OutputStream.WriteAsync(new byte[0], 0, 0);
                    response.Close();
                    return;
                }

                var relayResponse = await tcs.Task;
                _pendingRequests.Remove(sessionId);

                OnLogMessage($"[응답 수신] {relayMessage.Method} {relayMessage.Url} - Status: {relayResponse.StatusCode} (SessionId: {sessionId})");

                // 응답 전송
                response.StatusCode = relayResponse.StatusCode ?? 500;
                response.StatusDescription = GetStatusDescription(relayResponse.StatusCode ?? 500);

                if (relayResponse.Headers != null)
                {
                    foreach (var header in relayResponse.Headers)
                    {
                        try
                        {
                            if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                            {
                                response.ContentType = header.Value;
                            }
                            else if (header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                            {
                                // Content-Length는 자동 설정됨
                            }
                            else if (!header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                            {
                                response.Headers[header.Key] = header.Value;
                            }
                        }
                        catch
                        {
                            // 일부 헤더는 설정할 수 없음 (무시)
                        }
                    }
                }

                if (!string.IsNullOrEmpty(relayResponse.Body))
                {
                    var bodyBytes = Encoding.UTF8.GetBytes(relayResponse.Body);
                    response.ContentLength64 = bodyBytes.Length;
                    await response.OutputStream.WriteAsync(bodyBytes, 0, bodyBytes.Length);
                }

                response.Close();
            }
            catch (Exception ex)
            {
                OnLogMessage($"[요청 오류] 요청 처리 오류: {ex.Message}");
                try
                {
                    response.StatusCode = 500;
                    response.StatusDescription = "Internal Server Error";
                    response.Close();
                }
                catch { }
            }
        }

        /// <summary>
        /// CONNECT 요청 처리 (HTTPS 터널링)
        /// </summary>
        private async Task HandleConnectRequestAsync(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                // CONNECT 요청에 대한 200 OK 응답
                response.StatusCode = 200;
                response.StatusDescription = "Connection Established";
                response.Headers.Add("Connection", "keep-alive");
                await response.OutputStream.FlushAsync();

                // 터널링은 복잡하므로, 여기서는 간단히 처리
                // 실제로는 스트림을 Relay로 전달해야 함
                OnLogMessage($"[CONNECT] HTTPS 터널링 요청: {request.RawUrl}");
                response.Close();
            }
            catch (Exception ex)
            {
                OnLogMessage($"[CONNECT 오류] CONNECT 처리 오류: {ex.Message}");
                try
                {
                    response.StatusCode = 502;
                    response.Close();
                }
                catch { }
            }
        }

        /// <summary>
        /// Relay Server로 메시지 전송
        /// </summary>
        private async Task SendMessageAsync(RelayMessage message)
        {
            if (_webSocket?.State != WebSocketState.Open)
            {
                throw new InvalidOperationException("WebSocket이 연결되지 않았습니다.");
            }

            var json = SerializeMessage(message);
            var bytes = Encoding.UTF8.GetBytes(json);

            await _webSocket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                CancellationToken.None
            );
        }

        /// <summary>
        /// Relay Server로부터 메시지 수신
        /// </summary>
        private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];

            while (!cancellationToken.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
            {
                try
                {
                    var result = await _webSocket.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        cancellationToken
                    );

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        var message = DeserializeMessage(json);

                        if (message.Type == "RESPONSE")
                        {
                            // 응답을 대기 중인 요청에 전달
                            if (!string.IsNullOrEmpty(message.SessionId) && _pendingRequests.ContainsKey(message.SessionId))
                            {
                                _pendingRequests[message.SessionId].SetResult(message);
                            }
                        }
                        else if (message.Type == "PONG")
                        {
                            // PONG 수신 (연결 유지 확인)
                        }
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        OnConnectionStatusChanged(false);
                        OnLogMessage("[연결 종료] WebSocket 연결이 종료되었습니다.");
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        OnLogMessage($"[WebSocket 오류] 메시지 수신 오류: {ex.Message}");
                        await Task.Delay(1000, cancellationToken);
                    }
                }
            }
        }

        /// <summary>
        /// PING 전송 루프 (연결 유지)
        /// </summary>
        private async Task SendPingLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(30000, cancellationToken); // 30초마다

                    if (_webSocket?.State == WebSocketState.Open)
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
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        OnLogMessage($"[PING 오류] PING 전송 오류: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// 중지
        /// </summary>
        public async Task StopAsync()
        {
            if (!_isRunning)
                return;

            _isRunning = false;
            _cancellationTokenSource?.Cancel();

            try
            {
                _httpListener?.Stop();
                _httpListener?.Close();
            }
            catch { }

            try
            {
                if (_webSocket?.State == WebSocketState.Open)
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Shutting down", CancellationToken.None);
                }
            }
            catch { }

            try
            {
                _webSocket?.Dispose();
            }
            catch { }

            if (_receiveTask != null)
            {
                await Task.WhenAny(_receiveTask, Task.Delay(2000));
            }

            if (_pingTask != null)
            {
                await Task.WhenAny(_pingTask, Task.Delay(2000));
            }

            OnConnectionStatusChanged(false);
                OnLogMessage("[시스템] 프록시 서버가 중지되었습니다.");
        }

        /// <summary>
        /// 메시지 직렬화 (JSON)
        /// </summary>
        private string SerializeMessage(RelayMessage message)
        {
            var serializer = new JavaScriptSerializer();
            var dict = new Dictionary<string, object>
            {
                ["type"] = message.Type
            };

            if (!string.IsNullOrEmpty(message.SessionId))
                dict["sessionId"] = message.SessionId;

            if (!string.IsNullOrEmpty(message.Method))
                dict["method"] = message.Method;

            if (!string.IsNullOrEmpty(message.Url))
                dict["url"] = message.Url;

            if (message.Headers != null && message.Headers.Count > 0)
                dict["headers"] = message.Headers;

            if (!string.IsNullOrEmpty(message.Body))
                dict["body"] = message.Body;

            if (message.StatusCode.HasValue)
                dict["statusCode"] = message.StatusCode.Value;

            if (!string.IsNullOrEmpty(message.Error))
                dict["error"] = message.Error;

            return serializer.Serialize(dict);
        }

        /// <summary>
        /// 메시지 역직렬화 (JSON)
        /// </summary>
        private RelayMessage DeserializeMessage(string json)
        {
            try
            {
                var serializer = new JavaScriptSerializer();
                var dict = serializer.Deserialize<Dictionary<string, object>>(json);

                var message = new RelayMessage();

                if (dict.ContainsKey("type"))
                    message.Type = dict["type"]?.ToString();

                if (dict.ContainsKey("sessionId"))
                    message.SessionId = dict["sessionId"]?.ToString();

                if (dict.ContainsKey("method"))
                    message.Method = dict["method"]?.ToString();

                if (dict.ContainsKey("url"))
                    message.Url = dict["url"]?.ToString();

                if (dict.ContainsKey("headers"))
                {
                    var headersDict = dict["headers"] as Dictionary<string, object>;
                    if (headersDict != null)
                    {
                        message.Headers = new Dictionary<string, string>();
                        foreach (var kvp in headersDict)
                        {
                            message.Headers[kvp.Key] = kvp.Value?.ToString() ?? string.Empty;
                        }
                    }
                }

                if (dict.ContainsKey("body"))
                    message.Body = dict["body"]?.ToString();

                if (dict.ContainsKey("statusCode"))
                {
                    if (int.TryParse(dict["statusCode"]?.ToString(), out int statusCode))
                        message.StatusCode = statusCode;
                }

                if (dict.ContainsKey("error"))
                    message.Error = dict["error"]?.ToString();

                return message;
            }
            catch (Exception ex)
            {
                OnLogMessage($"[JSON 오류] JSON 파싱 오류: {ex.Message}");
                return new RelayMessage { Type = "ERROR", Error = $"JSON 파싱 실패: {ex.Message}" };
            }
        }

        /// <summary>
        /// 요청이 프록시를 통해 전달되어야 하는지 확인
        /// </summary>
        private bool ShouldProxyRequest(Uri url)
        {
            if (url == null)
            {
                OnLogMessage("[필터링 오류] URL이 null입니다");
                return false;
            }

            // 필터가 없으면 모든 도메인 허용
            if (_allowedDomains == null || _allowedDomains.Count == 0)
            {
                OnLogMessage($"[필터링] 필터가 설정되지 않아 모든 도메인 허용: {url.Host}");
                return true;
            }

            var host = url.Host;
            
            // 정확한 도메인 매칭 또는 서브도메인 매칭
            foreach (var allowedDomain in _allowedDomains)
            {
                if (host.Equals(allowedDomain, StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith("." + allowedDomain, StringComparison.OrdinalIgnoreCase))
                {
                    OnLogMessage($"[필터링 매칭] '{host}'이(가) 허용된 도메인 '{allowedDomain}'과(와) 매칭되었습니다");
                    return true;
                }
            }

            OnLogMessage($"[필터링 불일치] '{host}'이(가) 허용된 도메인 목록과 일치하지 않습니다. 허용 목록: {string.Join(", ", _allowedDomains)}");
            return false;
        }

        private string GetStatusDescription(int statusCode)
        {
            switch (statusCode)
            {
                case 200: return "OK";
                case 404: return "Not Found";
                case 500: return "Internal Server Error";
                case 502: return "Bad Gateway";
                case 504: return "Gateway Timeout";
                default: return "Unknown";
            }
        }

        private void OnLogMessage(string message)
        {
            LogMessage?.Invoke(this, $"[{DateTime.Now:HH:mm:ss}] {message}");
        }

        private void OnConnectionStatusChanged(bool connected)
        {
            ConnectionStatusChanged?.Invoke(this, connected);
        }

        public void Dispose()
        {
            StopAsync().Wait(3000);
            _cancellationTokenSource?.Dispose();
            _webSocket?.Dispose();
            _httpListener?.Close();
        }
    }
}

