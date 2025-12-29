using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
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
        private TcpListener _tcpListener;
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

        private void LogFilterInfo()
        {
            if (_allowedDomains != null && _allowedDomains.Count > 0)
            {
                OnLogMessage($"[필터 정보] 허용된 도메인 목록 ({_allowedDomains.Count}개): {string.Join(", ", _allowedDomains)}");
            }
            else
            {
                OnLogMessage("[필터 정보] 도메인 필터가 설정되지 않았습니다. 모든 도메인이 허용됩니다.");
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

                // TCP 프록시 서버 시작 (모든 요청 처리 - CONNECT 및 HTTP)
                StartTcpProxyServer();

                // 메시지 수신 루프 시작
                _receiveTask = Task.Run(() => ReceiveMessagesAsync(_cancellationTokenSource.Token));

                // PING 전송 루프 시작
                _pingTask = Task.Run(() => SendPingLoopAsync(_cancellationTokenSource.Token));

                // 필터 정보 로그
                LogFilterInfo();

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

                OnLogMessage($"[연결 시도] Relay Server 연결 시도 중: {wsUrl.Replace(_accessToken, "***")}");
                _webSocket = new ClientWebSocket();
                await _webSocket.ConnectAsync(new Uri(wsUrl), CancellationToken.None);
                
                // 연결 후 상태 확인
                var state = _webSocket.State;
                OnLogMessage($"[연결 후 상태] WebSocket 상태: {state}");
                
                if (state == WebSocketState.Open)
                {
                    OnConnectionStatusChanged(true);
                    OnLogMessage($"[연결 성공] Relay Server에 연결되었습니다: {wsUrl.Replace(_accessToken, "***")}");
                }
                else
                {
                    OnConnectionStatusChanged(false);
                    OnLogMessage($"[연결 실패] WebSocket 상태가 Open이 아닙니다: {state}");
                    throw new Exception($"WebSocket 연결 실패: 상태가 {state}입니다.");
                }
            }
            catch (Exception ex)
            {
                OnConnectionStatusChanged(false);
                OnLogMessage($"[연결 실패] Relay Server 연결 실패: {ex.Message}");
                if (ex.InnerException != null)
                {
                    OnLogMessage($"[연결 실패 상세] 내부 오류: {ex.InnerException.Message}");
                }
                throw;
            }
        }

        /// <summary>
        /// TCP 프록시 서버 시작 (CONNECT 요청 처리용)
        /// </summary>
        private void StartTcpProxyServer()
        {
            try
            {
                _tcpListener = new TcpListener(IPAddress.Any, _proxyPort);
                _tcpListener.Start();
                OnLogMessage($"[TCP 프록시 시작] TCP 프록시 서버가 시작되었습니다: 0.0.0.0:{_proxyPort}");
            }
            catch (Exception ex)
            {
                OnLogMessage($"[TCP 프록시 오류] TCP 프록시 서버 시작 실패: {ex.Message}");
                // TCP 리스너 시작 실패해도 HTTP 리스너는 계속 사용
                return;
            }

            Task.Run(async () =>
            {
                OnLogMessage("[TCP 프록시 대기] CONNECT 요청 대기 중...");

                while (_isRunning && _tcpListener != null)
                {
                    try
                    {
                        var client = await _tcpListener.AcceptTcpClientAsync();
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await HandleTcpClientAsync(client);
                            }
                            catch (Exception ex)
                            {
                                OnLogMessage($"[TCP 클라이언트 처리 오류] {ex.Message}");
                                try
                                {
                                    client.Close();
                                }
                                catch { }
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        if (_isRunning)
                        {
                            OnLogMessage($"[TCP 프록시 오류] 클라이언트 수락 오류: {ex.Message}");
                        }
                    }
                }
            });
        }

        /// <summary>
        /// TCP 클라이언트 처리 (CONNECT 요청 파싱 및 터널링)
        /// </summary>
        private async Task HandleTcpClientAsync(TcpClient client)
        {
            var stream = client.GetStream();
            var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
            var writer = new StreamWriter(stream, Encoding.ASCII, 1024, true) { AutoFlush = true };

            try
            {
                // 첫 번째 줄 읽기 (요청 라인)
                var firstLine = await reader.ReadLineAsync();
                if (string.IsNullOrEmpty(firstLine))
                {
                    OnLogMessage("[TCP] 빈 요청 수신");
                    await writer.WriteLineAsync("HTTP/1.1 400 Bad Request\r\n");
                    return;
                }

                OnLogMessage($"[TCP] 요청 수신: {firstLine}");

                // 요청 파싱
                var parts = firstLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                {
                    OnLogMessage($"[TCP] 잘못된 요청 형식: {firstLine}");
                    await writer.WriteLineAsync("HTTP/1.1 400 Bad Request\r\n");
                    return;
                }

                var method = parts[0];
                var target = parts[1];

                // CONNECT 요청 처리
                if (method.Equals("CONNECT", StringComparison.OrdinalIgnoreCase))
                {

                    // CONNECT 요청 파싱: "CONNECT host:port HTTP/1.1"
                    var targetParts = target.Split(':');
                    var targetHost = targetParts[0];
                    var targetPort = 443;

                    if (targetParts.Length > 1 && int.TryParse(targetParts[1], out int port))
                    {
                        targetPort = port;
                    }

                    OnLogMessage($"[TCP CONNECT] 대상: {targetHost}:{targetPort}");
                    OnLogMessage($"[TCP CONNECT] HTTPS는 필터링하지 않고 항상 직접 터널링합니다");

                    // 나머지 헤더 읽기 (무시)
                    string line;
                    while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
                    {
                        // 헤더 읽기 (빈 줄까지)
                    }

                    // HTTPS는 필터링 체크 없이 항상 직접 터널링
                    OnLogMessage($"[TCP CONNECT 직접] {targetHost}:{targetPort} - HTTPS는 필터링 대상이 아니므로 직접 터널링");
                    
                    // 대상 서버에 연결
                    using (var targetClient = new TcpClient())
                    {
                        await targetClient.ConnectAsync(targetHost, targetPort);
                        OnLogMessage($"[TCP CONNECT] 대상 서버 연결 성공: {targetHost}:{targetPort}");

                        // CONNECT 성공 응답
                        await writer.WriteLineAsync("HTTP/1.1 200 Connection Established\r\n");
                        await writer.FlushAsync();

                        // 양방향 스트림 복사
                        var targetStream = targetClient.GetStream();
                        var clientStream = client.GetStream();

                        // 클라이언트 -> 서버
                        var copyToServer = Task.Run(async () =>
                        {
                            try
                            {
                                var buffer = new byte[8192];
                                int bytesRead;
                                while ((bytesRead = await clientStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                                {
                                    await targetStream.WriteAsync(buffer, 0, bytesRead);
                                    await targetStream.FlushAsync();
                                }
                            }
                            catch (ObjectDisposedException)
                            {
                                // 스트림이 이미 닫힌 경우 - 정상 종료
                            }
                            catch (Exception ex)
                            {
                                // 다른 예외만 로그
                                if (!ex.Message.Contains("삭제된 개체") && !ex.Message.Contains("ObjectDisposed"))
                                {
                                    OnLogMessage($"[TCP CONNECT] 클라이언트->서버 스트림 오류: {ex.Message}");
                                }
                            }
                        });

                        // 서버 -> 클라이언트
                        var copyToClient = Task.Run(async () =>
                        {
                            try
                            {
                                var buffer = new byte[8192];
                                int bytesRead;
                                while ((bytesRead = await targetStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                                {
                                    await clientStream.WriteAsync(buffer, 0, bytesRead);
                                    await clientStream.FlushAsync();
                                }
                            }
                            catch (ObjectDisposedException)
                            {
                                // 스트림이 이미 닫힌 경우 - 정상 종료
                            }
                            catch (Exception ex)
                            {
                                // 다른 예외만 로그
                                if (!ex.Message.Contains("삭제된 개체") && !ex.Message.Contains("ObjectDisposed"))
                                {
                                    OnLogMessage($"[TCP CONNECT] 서버->클라이언트 스트림 오류: {ex.Message}");
                                }
                            }
                        });

                        // 양방향 중 하나라도 종료되면 대기
                        try
                        {
                            await Task.WhenAny(copyToServer, copyToClient);
                        }
                        catch { }
                        
                        // 스트림 정리
                        try
                        {
                            targetStream?.Close();
                        }
                        catch { }
                        
                        OnLogMessage($"[TCP CONNECT] 터널링 종료: {targetHost}:{targetPort}");
                    }
                    return;
                }
                else
                {
                    // HTTP 요청 처리 (GET, POST 등)
                    await HandleHttpRequestAsync(client, stream, reader, writer, method, target, firstLine);
                }
            }
            catch (Exception ex)
            {
                OnLogMessage($"[TCP 오류] 처리 오류: {ex.Message}");
                try
                {
                    await writer.WriteLineAsync("HTTP/1.1 502 Bad Gateway\r\n");
                }
                catch { }
            }
            finally
            {
                try
                {
                    client.Close();
                }
                catch { }
            }
        }

        /// <summary>
        /// HTTP 요청 처리 (TcpListener에서 받은 HTTP 요청)
        /// </summary>
        private async Task HandleHttpRequestAsync(TcpClient client, NetworkStream stream, StreamReader reader, StreamWriter writer, string method, string target, string firstLine)
        {
            try
            {
                // 헤더 먼저 읽기 (URL 파싱에 필요할 수 있음)
                var requestHeaders = new Dictionary<string, string>();
                string headerLine;
                while (!string.IsNullOrEmpty(headerLine = await reader.ReadLineAsync()))
                {
                    var colonIndex = headerLine.IndexOf(':');
                    if (colonIndex > 0)
                    {
                        var key = headerLine.Substring(0, colonIndex).Trim();
                        var value = headerLine.Substring(colonIndex + 1).Trim();
                        requestHeaders[key] = value;
                    }
                }

                // URL 파싱: "http://host:port/path" 또는 "http://host/path"
                Uri requestUri;
                if (Uri.TryCreate(target, UriKind.Absolute, out requestUri))
                {
                    // 절대 URL
                }
                else if (target.StartsWith("/"))
                {
                    // 상대 URL - Host 헤더에서 호스트 추출
                    if (requestHeaders.ContainsKey("Host"))
                    {
                        var host = requestHeaders["Host"];
                        if (host.Contains(":"))
                        {
                            var parts = host.Split(':');
                            requestUri = new Uri($"http://{parts[0]}:{parts[1]}{target}");
                        }
                        else
                        {
                            requestUri = new Uri($"http://{host}{target}");
                        }
                    }
                    else
                    {
                        OnLogMessage($"[TCP HTTP] Host 헤더가 없습니다: {firstLine}");
                        await writer.WriteLineAsync("HTTP/1.1 400 Bad Request\r\n");
                        return;
                    }
                }
                else
                {
                    OnLogMessage($"[TCP HTTP] 잘못된 URL 형식: {target}");
                    await writer.WriteLineAsync("HTTP/1.1 400 Bad Request\r\n");
                    return;
                }

                OnLogMessage($"[TCP HTTP] 요청 처리: {method} {requestUri}");

                // 필터링 체크
                bool shouldProxy = ShouldProxyRequest(requestUri);
                
                if (!shouldProxy)
                {
                    // 필터링 대상이 아니면 직접 통과
                    OnLogMessage($"[TCP HTTP 직접] {method} {requestUri} - 직접 통과");
                    await HandleDirectHttpRequestAsync(client, stream, requestHeaders, method, requestUri);
                    return;
                }

                // 필터링 대상이면 Relay Server 경유
                OnLogMessage($"[TCP HTTP Relay] {method} {requestUri} - Relay Server 경유");

                // 요청 본문 읽기 (있는 경우)
                string requestBody = null;
                if (requestHeaders.ContainsKey("Content-Length"))
                {
                    var contentLength = int.Parse(requestHeaders["Content-Length"]);
                    if (contentLength > 0 && contentLength < 10 * 1024 * 1024) // 10MB 제한
                    {
                        var bodyBytes = new byte[contentLength];
                        var totalRead = 0;
                        while (totalRead < contentLength)
                        {
                            var bytesRead = await stream.ReadAsync(bodyBytes, totalRead, contentLength - totalRead);
                            if (bytesRead == 0) break;
                            totalRead += bytesRead;
                        }
                        requestBody = Encoding.UTF8.GetString(bodyBytes, 0, totalRead);
                    }
                }

                // Relay Server로 전송
                var sessionId = Guid.NewGuid().ToString();
                var relayMessage = new RelayMessage
                {
                    Type = "REQUEST",
                    SessionId = sessionId,
                    Method = method,
                    Url = requestUri.ToString(),
                    Headers = requestHeaders,
                    Body = requestBody
                };

                // WebSocket 연결 상태 확인
                if (_webSocket == null || _webSocket.State != WebSocketState.Open)
                {
                    OnLogMessage($"[TCP HTTP Relay 실패] WebSocket이 연결되지 않았습니다.");
                    await writer.WriteLineAsync("HTTP/1.1 502 Bad Gateway\r\n");
                    return;
                }

                // Relay Server로 전송
                await SendMessageAsync(relayMessage);

                // 응답 대기
                var tcs = new TaskCompletionSource<RelayMessage>();
                lock (_pendingRequests)
                {
                    _pendingRequests[sessionId] = tcs;
                }

                try
                {
                    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(60));
                    var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

                    if (completedTask == timeoutTask)
                    {
                        lock (_pendingRequests)
                        {
                            _pendingRequests.Remove(sessionId);
                        }
                        await writer.WriteLineAsync("HTTP/1.1 504 Gateway Timeout\r\n");
                        return;
                    }

                    var relayResponse = await tcs.Task;
                    lock (_pendingRequests)
                    {
                        _pendingRequests.Remove(sessionId);
                    }

                    // 응답 전송
                    var statusCode = relayResponse.StatusCode ?? 500;
                    await writer.WriteLineAsync($"HTTP/1.1 {statusCode} {GetStatusDescription(statusCode)}\r\n");
                    
                    if (relayResponse.Headers != null)
                    {
                        foreach (var header in relayResponse.Headers)
                        {
                            if (!header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) &&
                                !header.Key.Equals("Connection", StringComparison.OrdinalIgnoreCase))
                            {
                                await writer.WriteLineAsync($"{header.Key}: {header.Value}\r\n");
                            }
                        }
                    }
                    
                    await writer.WriteLineAsync("\r\n");
                    
                    if (!string.IsNullOrEmpty(relayResponse.Body))
                    {
                        var bodyBytes = Encoding.UTF8.GetBytes(relayResponse.Body);
                        await stream.WriteAsync(bodyBytes, 0, bodyBytes.Length);
                    }
                }
                catch (Exception ex)
                {
                    lock (_pendingRequests)
                    {
                        _pendingRequests.Remove(sessionId);
                    }
                    OnLogMessage($"[TCP HTTP Relay 오류] 응답 처리 오류: {ex.Message}");
                    await writer.WriteLineAsync("HTTP/1.1 502 Bad Gateway\r\n");
                }
            }
            catch (Exception ex)
            {
                OnLogMessage($"[TCP HTTP 오류] 처리 오류: {ex.Message}");
                try
                {
                    await writer.WriteLineAsync("HTTP/1.1 500 Internal Server Error\r\n");
                }
                catch { }
            }
        }

        /// <summary>
        /// 직접 HTTP 요청 처리 (필터링 대상이 아닌 경우)
        /// </summary>
        private async Task HandleDirectHttpRequestAsync(TcpClient client, NetworkStream stream, Dictionary<string, string> headers, string method, Uri requestUri)
        {
            var writer = new StreamWriter(stream, Encoding.ASCII, 1024, true) { AutoFlush = true };
            
            try
            {

                // HttpClient로 직접 요청
                var handler = new HttpClientHandler
                {
                    UseProxy = false,
                    Proxy = null
                };
                
                using (var httpClient = new HttpClient(handler))
                {
                    httpClient.Timeout = TimeSpan.FromSeconds(30);
                    
                    var httpRequest = new HttpRequestMessage(new HttpMethod(method), requestUri);
                    
                    // 헤더 복사
                    foreach (var header in headers)
                    {
                        if (!header.Key.Equals("Proxy-", StringComparison.OrdinalIgnoreCase) &&
                            !header.Key.Equals("Connection", StringComparison.OrdinalIgnoreCase) &&
                            !header.Key.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase) &&
                            !header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                httpRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
                            }
                            catch { }
                        }
                    }

                    // 요청 본문
                    if (headers.ContainsKey("Content-Length"))
                    {
                        var contentLength = int.Parse(headers["Content-Length"]);
                        if (contentLength > 0 && contentLength < 10 * 1024 * 1024)
                        {
                            var bodyBytes = new byte[contentLength];
                            var totalRead = 0;
                            while (totalRead < contentLength)
                            {
                                var bytesRead = await stream.ReadAsync(bodyBytes, totalRead, contentLength - totalRead);
                                if (bytesRead == 0) break;
                                totalRead += bytesRead;
                            }
                            httpRequest.Content = new ByteArrayContent(bodyBytes, 0, totalRead);
                            if (headers.ContainsKey("Content-Type"))
                            {
                                httpRequest.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(headers["Content-Type"]);
                            }
                        }
                    }

                    var httpResponse = await httpClient.SendAsync(httpRequest);
                    
                    // 응답 전송
                    await writer.WriteLineAsync($"HTTP/1.1 {(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}\r\n");
                    
                    foreach (var header in httpResponse.Headers)
                    {
                        if (!header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                        {
                            await writer.WriteLineAsync($"{header.Key}: {string.Join(", ", header.Value)}\r\n");
                        }
                    }
                    
                    foreach (var header in httpResponse.Content.Headers)
                    {
                        if (!header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                        {
                            await writer.WriteLineAsync($"{header.Key}: {string.Join(", ", header.Value)}\r\n");
                        }
                    }
                    
                    await writer.WriteLineAsync("\r\n");
                    
                    var responseBody = await httpResponse.Content.ReadAsByteArrayAsync();
                    await stream.WriteAsync(responseBody, 0, responseBody.Length);
                }
            }
            catch (Exception ex)
            {
                OnLogMessage($"[TCP HTTP 직접 오류] 처리 오류: {ex.Message}");
                try
                {
                    await writer.WriteLineAsync("HTTP/1.1 502 Bad Gateway\r\n");
                }
                catch { }
            }
        }

        /// <summary>
        /// HTTP 프록시 서버 시작
        /// </summary>
        private void StartProxyServer()
        {
            try
            {
                _httpListener = new HttpListener();
                // localhost와 127.0.0.1 모두 리스닝
                _httpListener.Prefixes.Add($"http://localhost:{_proxyPort}/");
                _httpListener.Prefixes.Add($"http://127.0.0.1:{_proxyPort}/");
                _httpListener.Start();
                OnLogMessage($"[프록시 시작] HTTP 프록시 서버가 시작되었습니다: http://localhost:{_proxyPort} 및 http://127.0.0.1:{_proxyPort}");
                OnLogMessage($"[프록시 설정] 브라우저 프록시를 127.0.0.1:{_proxyPort}로 설정하세요");
            }
            catch (Exception ex)
            {
                OnLogMessage($"[프록시 오류] 프록시 서버 시작 실패: {ex.Message}");
                throw;
            }

            Task.Run(async () =>
            {
                OnLogMessage("[프록시 대기] 요청 대기 중... (브라우저에서 네이버 등에 접속해보세요)");

                while (_isRunning && _httpListener.IsListening)
                {
                    try
                    {
                        var context = await _httpListener.GetContextAsync();
                        var request = context.Request;
                        OnLogMessage($"[프록시 수신] 새로운 요청이 들어왔습니다! Method: {request.HttpMethod}, RawUrl: {request.RawUrl ?? "null"}, Url: {request.Url?.ToString() ?? "null"}");
                        
                        // CONNECT 요청인 경우 즉시 로그
                        if (request.HttpMethod == "CONNECT")
                        {
                            OnLogMessage($"[CONNECT 수신] CONNECT 요청 수신: {request.RawUrl}");
                        }
                        
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await HandleProxyRequestAsync(context);
                            }
                            catch (Exception ex)
                            {
                                OnLogMessage($"[요청 처리 오류] HandleProxyRequestAsync 예외: {ex.Message}");
                                if (ex.InnerException != null)
                                {
                                    OnLogMessage($"[요청 처리 오류 상세] 내부 오류: {ex.InnerException.Message}");
                                }
                                OnLogMessage($"[요청 처리 오류 상세] 스택 트레이스: {ex.StackTrace}");
                                
                                // 예외 발생 시 응답 처리
                                try
                                {
                                    var response = context.Response;
                                    response.StatusCode = 500;
                                    response.StatusDescription = "Internal Server Error";
                                    response.ContentLength64 = 0;
                                    response.Close();
                                }
                                catch { }
                            }
                        });
                    }
                    catch (HttpListenerException ex)
                    {
                        if (_isRunning)
                        {
                            OnLogMessage($"[HttpListener 예외] HttpListener 예외 발생: {ex.Message} (ErrorCode: {ex.ErrorCode})");
                        }
                    }
                    catch (Exception ex)
                    {
                        if (_isRunning)
                        {
                            OnLogMessage($"[요청 오류] 프록시 요청 처리 오류: {ex.Message}");
                            if (ex.InnerException != null)
                            {
                                OnLogMessage($"[요청 오류 상세] 내부 오류: {ex.InnerException.Message}");
                            }
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
            var requestId = $"{request.RemoteEndPoint}_{DateTime.Now:HHmmss.fff}"; // 요청 추적용 ID

            try
            {
                // 모든 요청에 대해 로그 기록 (요청이 들어오는지 확인)
                // CONNECT 요청의 경우 request.Url이 null일 수 있으므로 RawUrl 사용
                var requestUrl = request.Url?.ToString() ?? request.RawUrl ?? "unknown";
                var requestHost = request.Url?.Host ?? (request.RawUrl?.Split(':')[0] ?? "null");
                OnLogMessage($"[요청 수신] [{requestId}] {request.HttpMethod} {requestUrl} - 도메인: {requestHost}");
                OnLogMessage($"[요청 상세] [{requestId}] User-Agent: {request.UserAgent ?? "없음"}, RemoteEndPoint: {request.RemoteEndPoint}");

                // CONNECT 메서드 처리 (HTTPS 터널링) - 필터링 체크 전에 처리
                if (request.HttpMethod == "CONNECT")
                {
                    OnLogMessage($"[CONNECT 감지] [{requestId}] CONNECT 요청 감지됨: {request.RawUrl}");
                    await HandleConnectRequestAsync(context);
                    return;
                }

                // 도메인 필터링 체크
                bool shouldProxy = ShouldProxyRequest(request.Url);
                
                if (!shouldProxy)
                {
                    // 필터링 대상이 아니면 직접 통과 (패싱)
                    OnLogMessage($"[직접 통과] {request.HttpMethod} {request.Url} - 도메인 '{request.Url?.Host}'이(가) 필터 목록에 없어 직접 통과합니다");
                    await HandleDirectRequestAsync(context);
                    return;
                }
                
                OnLogMessage($"[Relay 경유] {request.HttpMethod} {request.Url} - 도메인 '{request.Url?.Host}'이(가) 필터 목록에 있어 Relay Server를 경유합니다");

                // 프록시를 통한 요청의 URL 재구성 (프록시 포트 제거)
                Uri originalUrl = request.Url;
                string targetHost = originalUrl.Host;
                int targetPort = originalUrl.Port;
                string scheme = originalUrl.Scheme;
                string path = originalUrl.AbsolutePath;
                string query = originalUrl.Query;
                
                // 프록시 포트(8888)가 포함되어 있으면 제거하고 기본 포트 사용
                if (targetPort == _proxyPort)
                {
                    targetPort = scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? 443 : 80;
                    OnLogMessage($"[Relay 경유] 프록시 포트 감지됨, 기본 포트로 변경: {targetPort}");
                }
                
                // 실제 대상 서버 URL 재구성
                var targetUrlBuilder = new UriBuilder(scheme, targetHost, targetPort, path, query);
                var targetUrl = targetUrlBuilder.Uri.ToString();
                
                OnLogMessage($"[Relay 경유] URL 재구성 - 원본: {request.Url}, 대상: {targetUrl}");

                // 일반 HTTP 요청 처리 (Relay Server 경유)
                var sessionId = Guid.NewGuid().ToString();
                OnLogMessage($"[SessionId 생성] [{requestId}] SessionId: {sessionId} - 요청과 연결됨");
                var relayMessage = new RelayMessage
                {
                    Type = "REQUEST",
                    SessionId = sessionId,
                    Method = request.HttpMethod,
                    Url = targetUrl,
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

                // WebSocket 연결 상태 확인
                if (_webSocket == null || _webSocket.State != WebSocketState.Open)
                {
                    OnLogMessage($"[Relay 경유 실패] WebSocket이 연결되지 않았습니다. 상태: {_webSocket?.State ?? WebSocketState.None}");
                    response.StatusCode = 502;
                    response.StatusDescription = "Bad Gateway - WebSocket not connected";
                    response.Close();
                    return;
                }

                // Relay Server로 전송
                OnLogMessage($"[요청 전송] [{requestId}] {relayMessage.Method} {relayMessage.Url} (SessionId: {sessionId})");
                OnLogMessage($"[WebSocket 상태] [{requestId}] WebSocket 연결 상태: {_webSocket?.State}");
                
                try
                {
                    await SendMessageAsync(relayMessage);
                    OnLogMessage($"[요청 전송 성공] [{requestId}] Relay Server로 요청 전송 완료 (SessionId: {sessionId})");
                }
                catch (Exception ex)
                {
                    var errorDetails = ex.Message;
                    if (ex.InnerException != null)
                    {
                        errorDetails += $" (내부 오류: {ex.InnerException.Message})";
                    }
                    OnLogMessage($"[요청 전송 실패] Relay Server로 요청 전송 실패: {errorDetails}");
                    OnLogMessage($"[요청 전송 실패 상세] 스택 트레이스: {ex.StackTrace}");
                    response.StatusCode = 502;
                    response.StatusDescription = "Bad Gateway - Failed to send request to relay server";
                    response.Close();
                    return;
                }

                // 응답 대기
                var tcs = new TaskCompletionSource<RelayMessage>();
                lock (_pendingRequests)
                {
                    _pendingRequests[sessionId] = tcs;
                    OnLogMessage($"[응답 대기 시작] [{requestId}] SessionId: {sessionId} - _pendingRequests에 등록됨 (총 {_pendingRequests.Count}개 대기 중)");
                }

                try
                {
                    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(60));
                    var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

                    if (completedTask == timeoutTask)
                    {
                        // 타임아웃 시 TaskCompletionSource를 완료시켜서 무한 대기 방지
                        TaskCompletionSource<RelayMessage> timeoutTcs = null;
                        lock (_pendingRequests)
                        {
                            if (_pendingRequests.ContainsKey(sessionId))
                            {
                                timeoutTcs = _pendingRequests[sessionId];
                                _pendingRequests.Remove(sessionId);
                            }
                        }
                        
                        if (timeoutTcs != null)
                        {
                            // 타임아웃 응답 생성
                            var timeoutResponse = new RelayMessage
                            {
                                Type = "RESPONSE",
                                SessionId = sessionId,
                                StatusCode = 504,
                                Error = "Gateway Timeout - 60초 내 응답 없음"
                            };
                            
                            // TaskCompletionSource를 완료시켜서 무한 대기 방지
                            try
                            {
                                timeoutTcs.TrySetResult(timeoutResponse);
                            }
                            catch { }
                        }
                        
                        OnLogMessage($"[타임아웃] {relayMessage.Method} {relayMessage.Url} - 60초 내 응답 없음 (SessionId: {sessionId})");
                        response.StatusCode = 504; // Gateway Timeout
                        response.StatusDescription = "Gateway Timeout";
                        response.Headers.Set("Cache-Control", "no-cache, no-store, must-revalidate, max-age=0");
                        response.Headers.Set("Pragma", "no-cache");
                        response.Headers.Set("Expires", "0");
                        response.Headers.Set("Retry-After", "3600");
                        response.Headers.Set("Connection", "close");
                        response.ContentLength64 = 0;
                        try
                        {
                            await response.OutputStream.FlushAsync();
                        }
                        catch { }
                        response.Close();
                        return;
                    }

                    RelayMessage relayResponse;
                    try
                    {
                        OnLogMessage($"[응답 대기 중] [{requestId}] SessionId: {sessionId} - 응답 수신 대기 중...");
                        relayResponse = await tcs.Task;
                        OnLogMessage($"[응답 수신 완료] [{requestId}] SessionId: {sessionId} - 응답을 받았습니다! StatusCode: {relayResponse.StatusCode}");
                        
                        // 응답을 받았으므로 _pendingRequests에서 제거 (이미 ReceiveMessagesAsync에서 제거되었을 수 있음)
                        lock (_pendingRequests)
                        {
                            if (_pendingRequests.ContainsKey(sessionId))
                            {
                                _pendingRequests.Remove(sessionId);
                                OnLogMessage($"[응답 처리] [{requestId}] SessionId: {sessionId} - _pendingRequests에서 제거됨");
                            }
                            else
                            {
                                OnLogMessage($"[응답 처리] [{requestId}] SessionId: {sessionId} - _pendingRequests에 없음 (이미 제거됨)");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // 예외 발생 시에도 _pendingRequests에서 제거
                        if (_pendingRequests.ContainsKey(sessionId))
                        {
                            _pendingRequests.Remove(sessionId);
                        }
                        OnLogMessage($"[응답 수신 오류] 응답 수신 중 오류 발생: {ex.Message}");
                        if (ex.InnerException != null)
                        {
                            OnLogMessage($"[응답 수신 오류 상세] 내부 오류: {ex.InnerException.Message}");
                        }
                        response.StatusCode = 502;
                        response.StatusDescription = "Bad Gateway - Error receiving response";
                        response.Headers.Set("Cache-Control", "no-cache, no-store, must-revalidate, max-age=0");
                        response.Headers.Set("Pragma", "no-cache");
                        response.Headers.Set("Expires", "0");
                        response.Headers.Set("Retry-After", "3600");
                        response.Headers.Set("Connection", "close");
                        response.ContentLength64 = 0;
                        response.Close();
                        return;
                    }

                    OnLogMessage($"[응답 수신] [{requestId}] {relayMessage.Method} {relayMessage.Url} - Status: {relayResponse.StatusCode} (SessionId: {sessionId})");
                    if (!string.IsNullOrEmpty(relayResponse.Error))
                    {
                        OnLogMessage($"[응답 오류 메시지] [{requestId}] {relayResponse.Error}");
                    }

                    // 응답 전송 (브라우저에 전달)
                    OnLogMessage($"[응답 전송 시작] [{requestId}] SessionId: {sessionId} - 브라우저({request.RemoteEndPoint})에 응답 전송 시작");
                    var statusCode = relayResponse.StatusCode ?? 500;
                    response.StatusCode = statusCode;
                    response.StatusDescription = GetStatusDescription(statusCode);

                    // 기본 Content-Type 설정 (헤더에 없을 경우)
                    string contentType = null;
                    
                    if (relayResponse.Headers != null)
                    {
                        foreach (var header in relayResponse.Headers)
                        {
                            try
                            {
                                if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                                {
                                    contentType = header.Value;
                                    response.ContentType = header.Value;
                                }
                                else if (header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                                {
                                    // Content-Length는 자동 설정됨
                                }
                                else if (!header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) &&
                                         !header.Key.Equals("Connection", StringComparison.OrdinalIgnoreCase))
                                {
                                    response.Headers[header.Key] = header.Value;
                                }
                            }
                            catch (Exception ex)
                            {
                                OnLogMessage($"[헤더 설정 실패] {header.Key}: {ex.Message}");
                            }
                        }
                    }

                    // 응답 본문 처리
                    byte[] bodyBytes = null;
                    if (!string.IsNullOrEmpty(relayResponse.Body))
                    {
                        bodyBytes = Encoding.UTF8.GetBytes(relayResponse.Body);
                    }
                    else
                    {
                        // 5xx 에러이고 본문이 없으면 에러 메시지를 본문으로 포함
                        if (statusCode >= 500 && !string.IsNullOrEmpty(relayResponse.Error))
                        {
                            var errorBody = $"Internal Server Error: {relayResponse.Error}";
                            bodyBytes = Encoding.UTF8.GetBytes(errorBody);
                            OnLogMessage($"[5xx 에러 본문] 에러 메시지를 본문에 포함: {relayResponse.Error}");
                        }
                        else
                        {
                            bodyBytes = new byte[0];
                        }
                    }

                    // Content-Type이 없으면 기본값 설정
                    if (string.IsNullOrEmpty(contentType))
                    {
                        if (bodyBytes.Length > 0)
                        {
                            response.ContentType = "text/html; charset=utf-8";
                        }
                        else
                        {
                            response.ContentType = "text/plain; charset=utf-8";
                        }
                    }

                    response.ContentLength64 = bodyBytes.Length;
                    
                    // 5xx 에러 시 브라우저 재시도 방지 헤더 추가
                    if (statusCode >= 500)
                    {
                        try
                        {
                            // 브라우저 재시도 방지를 위한 강력한 헤더 설정
                            response.Headers.Set("Cache-Control", "no-cache, no-store, must-revalidate, max-age=0");
                            response.Headers.Set("Pragma", "no-cache");
                            response.Headers.Set("Expires", "0");
                            // Retry-After 헤더 추가 (브라우저에게 재시도하지 말라고 명시)
                            response.Headers.Set("Retry-After", "3600"); // 1시간 후 재시도 (실제로는 재시도 안 함)
                            // Connection: close로 연결 종료
                            response.Headers.Set("Connection", "close");
                            // X-Error-Message 헤더 추가 (디버깅용)
                            if (!string.IsNullOrEmpty(relayResponse.Error))
                            {
                                try
                                {
                                    response.Headers.Set("X-Error-Message", relayResponse.Error);
                                }
                                catch { }
                            }
                        }
                        catch (Exception ex)
                        {
                            OnLogMessage($"[헤더 설정 실패] 5xx 에러 헤더 설정 실패: {ex.Message}");
                        }
                    }
                    
                    // 응답 본문 전송
                    try
                    {
                        if (bodyBytes.Length > 0)
                        {
                            await response.OutputStream.WriteAsync(bodyBytes, 0, bodyBytes.Length);
                            OnLogMessage($"[응답 본문 전송] {bodyBytes.Length} bytes 전송 완료");
                        }
                        else
                        {
                            // 본문이 없어도 최소한의 에러 메시지 포함 (5xx 에러인 경우)
                            if (statusCode >= 500)
                            {
                                var errorMsg = $"HTTP {statusCode} Error";
                                if (!string.IsNullOrEmpty(relayResponse.Error))
                                {
                                    errorMsg = $"HTTP {statusCode} Error: {relayResponse.Error}";
                                }
                                var errorBytes = Encoding.UTF8.GetBytes(errorMsg);
                                response.ContentLength64 = errorBytes.Length;
                                await response.OutputStream.WriteAsync(errorBytes, 0, errorBytes.Length);
                                OnLogMessage($"[5xx 에러 본문 추가] {errorBytes.Length} bytes 에러 메시지 전송");
                            }
                        }
                        
                        // 응답 스트림 플러시 및 닫기
                        await response.OutputStream.FlushAsync();
                        OnLogMessage($"[응답 플러시 완료] Status: {statusCode}");
                        
                        // 응답 닫기 (이것이 실제로 브라우저에 응답을 전송함)
                        response.Close();
                        OnLogMessage($"[응답 전송 완료] [{requestId}] {relayMessage.Method} {relayMessage.Url} - Status: {statusCode}, Body: {response.ContentLength64} bytes, Content-Type: {response.ContentType} (SessionId: {sessionId})");
                        OnLogMessage($"[응답 전송 완료] [{requestId}] 브라우저({request.RemoteEndPoint})에 응답이 전송되었습니다!");
                    }
                    catch (Exception ex)
                    {
                        OnLogMessage($"[응답 전송 오류] 응답 전송 중 오류 발생: {ex.Message}");
                        try
                        {
                            response.Abort(); // 응답 중단
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    // 예외 발생 시에도 _pendingRequests에서 제거
                    lock (_pendingRequests)
                    {
                        _pendingRequests.Remove(sessionId);
                    }
                    OnLogMessage($"[응답 처리 오류] 응답 처리 중 오류 발생: {ex.Message}");
                    try
                    {
                        response.StatusCode = 502;
                        response.StatusDescription = "Bad Gateway - Error processing response";
                        response.Headers.Set("Cache-Control", "no-cache, no-store, must-revalidate, max-age=0");
                        response.Headers.Set("Pragma", "no-cache");
                        response.Headers.Set("Expires", "0");
                        response.Headers.Set("Retry-After", "3600");
                        response.Headers.Set("Connection", "close");
                        response.ContentLength64 = 0;
                        response.Close();
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                OnLogMessage($"[요청 오류] 요청 처리 오류: {ex.Message}");
                try
                {
                    response.StatusCode = 500;
                    response.StatusDescription = "Internal Server Error";
                    response.Headers.Set("Cache-Control", "no-cache, no-store, must-revalidate, max-age=0");
                    response.Headers.Set("Pragma", "no-cache");
                    response.Headers.Set("Expires", "0");
                    response.Headers.Set("Retry-After", "3600");
                    response.Headers.Set("Connection", "close");
                    response.ContentLength64 = 0;
                    response.Close();
                }
                catch { }
            }
        }

        /// <summary>
        /// 직접 요청 처리 (필터링 대상이 아닌 경우)
        /// </summary>
        private async Task HandleDirectRequestAsync(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                // 프록시를 명시적으로 비활성화하여 무한 루프 방지
                var handler = new HttpClientHandler
                {
                    UseProxy = false,
                    Proxy = null
                };
                
                using (var httpClient = new HttpClient(handler))
                {
                    // 타임아웃 설정 (30초)
                    httpClient.Timeout = TimeSpan.FromSeconds(30);
                    
                    // 프록시를 통한 요청의 URL 재구성
                    // request.Url은 프록시 서버의 URL을 포함할 수 있으므로, 실제 대상 서버 URL로 재구성
                    Uri requestUrl = request.Url;
                    string targetHost = requestUrl.Host;
                    int targetPort = requestUrl.Port;
                    string scheme = requestUrl.Scheme;
                    string path = requestUrl.AbsolutePath;
                    string query = requestUrl.Query;
                    
                    // 프록시 포트(8888)가 포함되어 있으면 제거하고 기본 포트 사용
                    if (targetPort == _proxyPort)
                    {
                        targetPort = scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? 443 : 80;
                        OnLogMessage($"[직접 요청] 프록시 포트 감지됨, 기본 포트로 변경: {targetPort}");
                    }
                    
                    // 실제 대상 서버 URL 재구성
                    var targetUrlBuilder = new UriBuilder(scheme, targetHost, targetPort, path, query);
                    requestUrl = targetUrlBuilder.Uri;
                    
                    OnLogMessage($"[직접 요청] 요청 URL 분석 - 원본: {request.Url}, 재구성: {requestUrl}");
                    OnLogMessage($"[직접 요청] 대상 서버 - Scheme: {requestUrl.Scheme}, Host: {requestUrl.Host}, Port: {requestUrl.Port}, Path: {requestUrl.AbsolutePath}");
                    
                    // 요청 생성
                    var httpRequest = new HttpRequestMessage(new HttpMethod(request.HttpMethod), requestUrl);

                    // 헤더 복사
                    foreach (string key in request.Headers.AllKeys)
                    {
                        if (!key.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase) &&
                            !key.Equals("Connection", StringComparison.OrdinalIgnoreCase) &&
                            !key.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase) &&
                            !key.Equals("Host", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                httpRequest.Headers.TryAddWithoutValidation(key, request.Headers[key]);
                            }
                            catch { }
                        }
                    }

                    // 요청 본문
                    if (request.HasEntityBody)
                    {
                        var contentLength = request.ContentLength64;
                        OnLogMessage($"[직접 요청] 요청 본문 크기: {contentLength} bytes");
                        
                        if (contentLength > 0 && contentLength < 10 * 1024 * 1024) // 10MB 제한
                        {
                            var bodyBytes = new byte[contentLength];
                            var bytesRead = 0;
                            var totalRead = 0;
                            
                            while (totalRead < contentLength && (bytesRead = await request.InputStream.ReadAsync(bodyBytes, totalRead, (int)(contentLength - totalRead))) > 0)
                            {
                                totalRead += bytesRead;
                            }
                            
                            if (totalRead > 0)
                            {
                                var contentType = request.ContentType ?? "application/octet-stream";
                                httpRequest.Content = new ByteArrayContent(bodyBytes, 0, totalRead);
                                httpRequest.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
                                OnLogMessage($"[직접 요청] 요청 본문 {totalRead} bytes 읽음");
                            }
                        }
                    }

                    // 직접 요청 수행
                    OnLogMessage($"[직접 요청] {request.HttpMethod} {request.Url} 직접 요청 시작");
                    var httpResponse = await httpClient.SendAsync(httpRequest);
                    OnLogMessage($"[직접 응답] {request.HttpMethod} {request.Url} - Status: {httpResponse.StatusCode}");

                    // 응답 헤더 복사
                    response.StatusCode = (int)httpResponse.StatusCode;
                    response.StatusDescription = httpResponse.ReasonPhrase;

                    foreach (var header in httpResponse.Headers)
                    {
                        try
                        {
                            if (header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                                continue;

                            response.Headers[header.Key] = string.Join(", ", header.Value);
                        }
                        catch { }
                    }

                    foreach (var header in httpResponse.Content.Headers)
                    {
                        try
                        {
                            if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                            {
                                response.ContentType = string.Join(", ", header.Value);
                            }
                            else if (header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                            {
                                // Content-Length는 자동 설정됨
                            }
                            else
                            {
                                response.Headers[header.Key] = string.Join(", ", header.Value);
                            }
                        }
                        catch { }
                    }

                    // 응답 본문 복사
                    var responseBody = await httpResponse.Content.ReadAsByteArrayAsync();
                    response.ContentLength64 = responseBody.Length;
                    await response.OutputStream.WriteAsync(responseBody, 0, responseBody.Length);

                    OnLogMessage($"[직접 응답 완료] {request.HttpMethod} {request.Url} - {responseBody.Length} bytes 전송");
                }

                response.Close();
            }
            catch (Exception ex)
            {
                var errorDetails = ex.Message;
                if (ex.InnerException != null)
                {
                    errorDetails += $" (내부 오류: {ex.InnerException.Message})";
                }
                OnLogMessage($"[직접 요청 오류] {request.HttpMethod} {request.Url} - 오류: {errorDetails}");
                OnLogMessage($"[직접 요청 오류 상세] 스택 트레이스: {ex.StackTrace}");
                try
                {
                    response.StatusCode = 502;
                    response.StatusDescription = "Bad Gateway";
                    response.Close();
                }
                catch { }
            }
        }

        /// <summary>
        /// 직접 CONNECT 처리 (필터링 대상이 아닌 경우)
        /// </summary>
        private async Task HandleDirectConnectAsync(HttpListenerContext context, string targetHost)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                // 직접 TCP 연결
                var port = 443; // HTTPS 기본 포트
                var rawUrl = request.RawUrl ?? "";
                
                if (rawUrl.Contains(":"))
                {
                    var parts = rawUrl.Split(':');
                    if (parts.Length > 1 && int.TryParse(parts[1], out int parsedPort))
                    {
                        port = parsedPort;
                    }
                }
                
                OnLogMessage($"[CONNECT 직접] {targetHost}:{port} 직접 연결 시작 (RawUrl: {rawUrl})");

                using (var tcpClient = new System.Net.Sockets.TcpClient())
                {
                    await tcpClient.ConnectAsync(targetHost, port);
                    OnLogMessage($"[CONNECT 직접 성공] {targetHost}:{port} 연결 성공");

                    // CONNECT 성공 응답 (헤더만 전송)
                    // HttpListener의 경우 CONNECT 요청에 대해 특별한 처리가 필요함
                    response.StatusCode = 200;
                    response.StatusDescription = "Connection Established";
                    response.Headers.Clear(); // 기존 헤더 제거
                    response.Headers.Add("Connection", "keep-alive");
                    response.SendChunked = false;
                    response.ContentLength64 = 0;
                    
                    // HttpListener에서 CONNECT 응답 헤더를 전송하려면
                    // OutputStream에 빈 바이트를 쓰거나, 헤더를 명시적으로 전송해야 함
                    // 여기서는 헤더만 전송하고 터널링을 시작함
                    var outputStream = response.OutputStream;
                    
                    // 헤더 전송을 위해 빈 바이트를 쓰거나 FlushAsync 호출
                    // HttpListener는 첫 Write/Flush 시 헤더를 전송함
                    await outputStream.FlushAsync();
                    OnLogMessage($"[CONNECT 직접] 응답 헤더 전송 완료, 터널링 시작");

                    // 스트림 터널링 (양방향)
                    var serverStream = tcpClient.GetStream();
                    var clientOutputStream = response.OutputStream;
                    var clientInputStream = request.InputStream;
                    
                    OnLogMessage($"[CONNECT 직접] 양방향 터널링 시작 - 서버 스트림: {serverStream.CanRead}/{serverStream.CanWrite}, 클라이언트 출력: {clientOutputStream.CanWrite}, 클라이언트 입력: {clientInputStream.CanRead}");

                    // 양방향 스트림 복사
                    // 서버 -> 클라이언트 (대상 서버에서 받은 데이터를 브라우저로 전송)
                    var copyToClient = Task.Run(async () =>
                    {
                        try
                        {
                            var buffer = new byte[8192];
                            int bytesRead;
                            int totalBytes = 0;
                            while ((bytesRead = await serverStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await clientOutputStream.WriteAsync(buffer, 0, bytesRead);
                                await clientOutputStream.FlushAsync();
                                totalBytes += bytesRead;
                            }
                            OnLogMessage($"[CONNECT 직접] 서버->클라이언트 스트림 종료 (총 {totalBytes} bytes 전송)");
                        }
                        catch (Exception ex)
                        {
                            OnLogMessage($"[CONNECT 직접] 서버->클라이언트 스트림 오류: {ex.Message}");
                            if (ex.InnerException != null)
                            {
                                OnLogMessage($"[CONNECT 직접] 서버->클라이언트 스트림 내부 오류: {ex.InnerException.Message}");
                            }
                        }
                    });

                    // 클라이언트 -> 서버 (브라우저에서 받은 데이터를 대상 서버로 전송)
                    var copyFromClient = Task.Run(async () =>
                    {
                        try
                        {
                            var buffer = new byte[8192];
                            int bytesRead;
                            int totalBytes = 0;
                            while ((bytesRead = await clientInputStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await serverStream.WriteAsync(buffer, 0, bytesRead);
                                await serverStream.FlushAsync();
                                totalBytes += bytesRead;
                            }
                            OnLogMessage($"[CONNECT 직접] 클라이언트->서버 스트림 종료 (총 {totalBytes} bytes 전송)");
                        }
                        catch (Exception ex)
                        {
                            OnLogMessage($"[CONNECT 직접] 클라이언트->서버 스트림 오류: {ex.Message}");
                            if (ex.InnerException != null)
                            {
                                OnLogMessage($"[CONNECT 직접] 클라이언트->서버 스트림 내부 오류: {ex.InnerException.Message}");
                            }
                        }
                    });

                    // 양방향 스트림 중 하나라도 종료되면 대기
                    await Task.WhenAny(copyToClient, copyFromClient);
                    OnLogMessage($"[CONNECT 직접 종료] {targetHost} 연결 종료");
                }
            }
            catch (Exception ex)
            {
                OnLogMessage($"[CONNECT 직접 오류] {targetHost} - 오류: {ex.Message}");
                if (ex.InnerException != null)
                {
                    OnLogMessage($"[CONNECT 직접 오류 상세] 내부 오류: {ex.InnerException.Message}");
                }
                try
                {
                    response.StatusCode = 502;
                    response.StatusDescription = "Bad Gateway";
                    response.ContentLength64 = 0;
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
                // CONNECT 요청의 RawUrl은 "www.naver.com:443" 형식
                var rawUrl = request.RawUrl ?? "";
                var targetHost = rawUrl.Split(':')[0];
                var targetPort = 443; // 기본 HTTPS 포트
                if (rawUrl.Contains(":"))
                {
                    var parts = rawUrl.Split(':');
                    if (parts.Length > 1 && int.TryParse(parts[1], out int parsedPort))
                    {
                        targetPort = parsedPort;
                    }
                }
                OnLogMessage($"[CONNECT] HTTPS CONNECT 요청 수신: {rawUrl} -> {targetHost}:{targetPort}");
                OnLogMessage($"[CONNECT] HTTPS는 필터링하지 않고 항상 직접 터널링합니다");

                // HTTPS는 필터링 체크 없이 항상 직접 터널링
                OnLogMessage($"[CONNECT 직접 터널링] {targetHost}:{targetPort} - HTTPS는 필터링 대상이 아니므로 직접 터널링");
                await HandleDirectConnectAsync(context, targetHost);
                return;
            }
            catch (Exception ex)
            {
                OnLogMessage($"[CONNECT 오류] CONNECT 처리 오류: {ex.Message}");
                try
                {
                    response.StatusCode = 502;
                    response.StatusDescription = "Bad Gateway";
                    response.Headers.Set("Cache-Control", "no-cache, no-store, must-revalidate, max-age=0");
                    response.Headers.Set("Pragma", "no-cache");
                    response.Headers.Set("Expires", "0");
                    response.Headers.Set("Retry-After", "3600");
                    response.Headers.Set("Connection", "close");
                    response.ContentLength64 = 0;
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
                var errorMsg = $"WebSocket이 연결되지 않았습니다. 현재 상태: {_webSocket?.State}";
                OnLogMessage($"[전송 실패] {errorMsg}");
                throw new InvalidOperationException(errorMsg);
            }

            try
            {
                var json = SerializeMessage(message);
                var bytes = Encoding.UTF8.GetBytes(json);

                OnLogMessage($"[WebSocket 전송] 메시지 전송 시도: {message.Type} (크기: {bytes.Length} bytes)");
                
                // CONNECT 메시지의 경우 JSON 내용도 로그에 출력
                if (message.Type == "CONNECT")
                {
                    OnLogMessage($"[CONNECT 메시지 내용] {json}");
                    OnLogMessage($"[CONNECT 메시지 상세] SessionId: {message.SessionId}, Url: {message.Url}");
                }

                await _webSocket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None
                );

                OnLogMessage($"[WebSocket 전송 성공] 메시지 전송 완료: {message.Type} (SessionId: {message.SessionId ?? "null"})");
            }
            catch (Exception ex)
            {
                OnLogMessage($"[WebSocket 전송 실패] 메시지 전송 실패: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Relay Server로부터 메시지 수신
        /// </summary>
        private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];
            OnLogMessage("[WebSocket 수신 루프 시작] ReceiveMessagesAsync 시작됨");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // WebSocket 상태 확인
                    if (_webSocket == null || _webSocket.State != WebSocketState.Open)
                    {
                        OnLogMessage($"[WebSocket 종료] WebSocket이 열려있지 않습니다. 상태: {_webSocket?.State ?? WebSocketState.None}");
                        break;
                    }

                    OnLogMessage($"[WebSocket 수신 대기] 메시지 수신 대기 중... (상태: {_webSocket.State})");
                    
                    var result = await _webSocket.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        cancellationToken
                    );
                    OnLogMessage($"[WebSocket 메시지 수신] MessageType: {result.MessageType}, Count: {result.Count}, EndOfMessage: {result.EndOfMessage}");

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        OnConnectionStatusChanged(false);
                        OnLogMessage("[연결 종료] WebSocket 연결이 종료되었습니다.");
                        break;
                    }

                    // 여러 청크로 나뉜 메시지를 수집하기 위한 버퍼
                    var messageBuffer = new List<byte>();
                    
                    // 첫 번째 청크 추가
                    if (result.MessageType == WebSocketMessageType.Text || result.MessageType == WebSocketMessageType.Binary)
                    {
                        messageBuffer.AddRange(buffer.Take(result.Count));
                    }
                    
                    // 메시지가 완전히 수신될 때까지 모든 청크 수집
                    while (!result.EndOfMessage)
                    {
                        result = await _webSocket.ReceiveAsync(
                            new ArraySegment<byte>(buffer),
                            cancellationToken
                        );
                        OnLogMessage($"[WebSocket 메시지 청크 수신] MessageType: {result.MessageType}, Count: {result.Count}, EndOfMessage: {result.EndOfMessage}");
                        
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            OnConnectionStatusChanged(false);
                            OnLogMessage("[연결 종료] WebSocket 연결이 종료되었습니다.");
                            return; // 외부 루프도 종료
                        }
                        
                        if (result.MessageType == WebSocketMessageType.Text || result.MessageType == WebSocketMessageType.Binary)
                        {
                            messageBuffer.AddRange(buffer.Take(result.Count));
                        }
                    }

                    // Close 메시지가 아닌 경우에만 처리
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var json = Encoding.UTF8.GetString(messageBuffer.ToArray());
                        OnLogMessage($"[WebSocket JSON 수신] JSON 길이: {json.Length}, 내용 (처음 200자): {json.Substring(0, Math.Min(200, json.Length))}");
                        var message = DeserializeMessage(json);
                        OnLogMessage($"[WebSocket 메시지 파싱 완료] Type: {message.Type}, SessionId: {message.SessionId ?? "null"}, StatusCode: {message.StatusCode}");

                        if (message.Type == "RESPONSE" || message.Type == "CONNECT_RESPONSE")
                        {
                            // 응답을 대기 중인 요청에 전달
                            OnLogMessage($"[WebSocket 응답 수신] Type: {message.Type}, SessionId: {message.SessionId}, StatusCode: {message.StatusCode}");
                            
                            TaskCompletionSource<RelayMessage> tcs = null;
                            lock (_pendingRequests)
                            {
                                OnLogMessage($"[WebSocket 응답 수신] SessionId: {message.SessionId}, StatusCode: {message.StatusCode} - _pendingRequests에서 찾는 중 (총 {_pendingRequests.Count}개 대기 중)");
                                if (!string.IsNullOrEmpty(message.SessionId) && _pendingRequests.ContainsKey(message.SessionId))
                                {
                                    tcs = _pendingRequests[message.SessionId];
                                    _pendingRequests.Remove(message.SessionId);
                                    OnLogMessage($"[WebSocket 응답 매칭] SessionId: {message.SessionId} - _pendingRequests에서 찾아서 제거함 (남은 대기: {_pendingRequests.Count}개)");
                                }
                                else
                                {
                                    OnLogMessage($"[WebSocket 응답 매칭 실패] SessionId: {message.SessionId} - _pendingRequests에 없음. 대기 중인 SessionId 목록: {string.Join(", ", _pendingRequests.Keys.Take(5))}");
                                }
                            }
                            
                            if (tcs != null)
                            {
                                // TaskCompletionSource를 완료시켜서 대기 중인 요청에 응답 전달
                                try
                                {
                                    tcs.TrySetResult(message);
                                    OnLogMessage($"[응답 매칭 성공] SessionId: {message.SessionId}, StatusCode: {message.StatusCode} - 대기 중인 요청에 응답 전달됨");
                                }
                                catch (Exception ex)
                                {
                                    OnLogMessage($"[응답 매칭 실패] SessionId: {message.SessionId}, 오류: {ex.Message}");
                                }
                            }
                            else
                            {
                                OnLogMessage($"[응답 매칭 실패] SessionId '{message.SessionId}'에 대한 대기 중인 요청이 없습니다. (이미 타임아웃되었거나 처리되었을 수 있음)");
                            }
                        }
                        else if (message.Type == "PONG")
                        {
                            // PONG 수신 (연결 유지 확인)
                            OnLogMessage("[PONG 수신] 연결 유지 확인");
                        }
                        else
                        {
                            OnLogMessage($"[알 수 없는 메시지 타입] Type: {message.Type}");
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
                    OnLogMessage("[WebSocket 종료] 취소 요청됨");
                    break;
                }
                catch (WebSocketException ex)
                {
                    OnLogMessage($"[WebSocket 예외] WebSocket 예외 발생: {ex.Message}, 상태: {_webSocket?.State ?? WebSocketState.None}");
                    if (_webSocket?.State != WebSocketState.Open)
                    {
                        OnConnectionStatusChanged(false);
                        break;
                    }
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        await Task.Delay(1000, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        OnLogMessage($"[WebSocket 오류] 메시지 수신 오류: {ex.Message}");
                        if (_webSocket?.State != WebSocketState.Open)
                        {
                            OnConnectionStatusChanged(false);
                            break;
                        }
                        await Task.Delay(1000, cancellationToken);
                    }
                    else
                    {
                        break;
                    }
                }
            }
            
            OnLogMessage("[WebSocket 수신 루프 종료] ReceiveMessagesAsync 종료");
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

            // 모든 대기 중인 요청 취소
            lock (_pendingRequests)
            {
                foreach (var kvp in _pendingRequests)
                {
                    try
                    {
                        // 타임아웃 응답으로 완료시켜서 무한 대기 방지
                        var cancelResponse = new RelayMessage
                        {
                            Type = "RESPONSE",
                            SessionId = kvp.Key,
                            StatusCode = 503,
                            Error = "Service Unavailable - Proxy server is shutting down"
                        };
                        kvp.Value.TrySetResult(cancelResponse);
                    }
                    catch { }
                }
                _pendingRequests.Clear();
            }

            try
            {
                _httpListener?.Stop();
                _httpListener?.Close();
            }
            catch { }

            try
            {
                _tcpListener?.Stop();
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

            var host = url.Host;
            OnLogMessage($"[필터링 체크] 도메인 체크 시작: '{host}'");

            // 필터가 없으면 모든 도메인 직접 통과 (Relay 경유 안 함)
            if (_allowedDomains == null || _allowedDomains.Count == 0)
            {
                OnLogMessage($"[필터링] 필터가 설정되지 않아 모든 도메인 직접 통과: {host}");
                return false;  // false = 직접 통과, true = Relay 경유
            }

            OnLogMessage($"[필터링] 허용된 도메인 목록 ({_allowedDomains.Count}개): {string.Join(", ", _allowedDomains)}");
            
            // 정확한 도메인 매칭 또는 서브도메인 매칭
            foreach (var allowedDomain in _allowedDomains)
            {
                var trimmedDomain = allowedDomain.Trim();
                
                // 정확한 매칭
                if (host.Equals(trimmedDomain, StringComparison.OrdinalIgnoreCase))
                {
                    OnLogMessage($"[필터링 매칭 성공] '{host}' == '{trimmedDomain}' (정확한 매칭)");
                    return true;
                }
                
                // 서브도메인 매칭 (예: www.naver.com이 naver.com과 매칭)
                if (host.EndsWith("." + trimmedDomain, StringComparison.OrdinalIgnoreCase))
                {
                    OnLogMessage($"[필터링 매칭 성공] '{host}' ends with '.{trimmedDomain}' (서브도메인 매칭)");
                    return true;
                }
            }

            OnLogMessage($"[필터링 불일치] '{host}'이(가) 허용된 도메인 목록과 일치하지 않습니다.");
            OnLogMessage($"[필터링 불일치] 허용 목록: {string.Join(", ", _allowedDomains)}");
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

