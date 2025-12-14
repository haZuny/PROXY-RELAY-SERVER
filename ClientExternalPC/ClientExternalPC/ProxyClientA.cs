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

                // HTTP 프록시 서버 시작
                StartProxyServer();

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
                        OnLogMessage("[프록시 수신] 새로운 요청이 들어왔습니다!");
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
                // 모든 요청에 대해 로그 기록 (요청이 들어오는지 확인)
                OnLogMessage($"[요청 수신] {request.HttpMethod} {request.Url} - 도메인: {request.Url?.Host ?? "null"}");
                OnLogMessage($"[요청 상세] User-Agent: {request.UserAgent ?? "없음"}, RemoteEndPoint: {request.RemoteEndPoint}");

                // CONNECT 메서드 처리 (HTTPS 터널링) - 필터링 체크 전에 처리
                if (request.HttpMethod == "CONNECT")
                {
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
                    OnLogMessage($"[Relay 경유] 프록시 포트 감지됨, 기본 포트로 변경: {targetPort}");
                }
                
                // 실제 대상 서버 URL 재구성
                var targetUrlBuilder = new UriBuilder(scheme, targetHost, targetPort, path, query);
                var targetUrl = targetUrlBuilder.Uri.ToString();
                
                OnLogMessage($"[Relay 경유] URL 재구성 - 원본: {request.Url}, 대상: {targetUrl}");

                // 일반 HTTP 요청 처리 (Relay Server 경유)
                var sessionId = Guid.NewGuid().ToString();
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
                OnLogMessage($"[요청 전송] {relayMessage.Method} {relayMessage.Url} (SessionId: {sessionId})");
                OnLogMessage($"[WebSocket 상태] WebSocket 연결 상태: {_webSocket?.State}");
                
                try
                {
                    await SendMessageAsync(relayMessage);
                    OnLogMessage($"[요청 전송 성공] Relay Server로 요청 전송 완료 (SessionId: {sessionId})");
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
                _pendingRequests[sessionId] = tcs;

                try
                {
                    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(60));
                    var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

                    if (completedTask == timeoutTask)
                    {
                        if (_pendingRequests.ContainsKey(sessionId))
                        {
                            _pendingRequests.Remove(sessionId);
                        }
                        OnLogMessage($"[타임아웃] {relayMessage.Method} {relayMessage.Url} - 60초 내 응답 없음 (SessionId: {sessionId})");
                        response.StatusCode = 504; // Gateway Timeout
                        response.StatusDescription = "Gateway Timeout";
                        try
                        {
                            await response.OutputStream.WriteAsync(new byte[0], 0, 0);
                        }
                        catch { }
                        response.Close();
                        return;
                    }

                    RelayMessage relayResponse;
                    try
                    {
                        relayResponse = await tcs.Task;
                    }
                    catch (Exception ex)
                    {
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
                        response.Close();
                        return;
                    }

                    if (_pendingRequests.ContainsKey(sessionId))
                    {
                        _pendingRequests.Remove(sessionId);
                    }

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
                    if (_pendingRequests.ContainsKey(sessionId))
                    {
                        _pendingRequests.Remove(sessionId);
                    }
                    OnLogMessage($"[응답 처리 오류] 응답 처리 중 오류 발생: {ex.Message}");
                    try
                    {
                        response.StatusCode = 502;
                        response.StatusDescription = "Bad Gateway - Error processing response";
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
                OnLogMessage($"[CONNECT 직접] {targetHost} 직접 연결 시작");
                
                var port = 443; // HTTPS 기본 포트
                if (request.RawUrl.Contains(":"))
                {
                    var parts = request.RawUrl.Split(':');
                    if (parts.Length > 1 && int.TryParse(parts[1], out int parsedPort))
                    {
                        port = parsedPort;
                    }
                }

                using (var tcpClient = new System.Net.Sockets.TcpClient())
                {
                    await tcpClient.ConnectAsync(targetHost, port);
                    OnLogMessage($"[CONNECT 직접 성공] {targetHost}:{port} 연결 성공");

                    // CONNECT 성공 응답
                    response.StatusCode = 200;
                    response.StatusDescription = "Connection Established";
                    response.Headers.Add("Connection", "keep-alive");
                    await response.OutputStream.FlushAsync();

                    // 스트림 터널링 (양방향)
                    var clientStream = tcpClient.GetStream();
                    var serverStream = response.OutputStream;

                    // 양방향 스트림 복사
                    var copyToServer = Task.Run(async () =>
                    {
                        try
                        {
                            var buffer = new byte[4096];
                            int bytesRead;
                            while ((bytesRead = await clientStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await serverStream.WriteAsync(buffer, 0, bytesRead);
                                await serverStream.FlushAsync();
                            }
                        }
                        catch { }
                    });

                    var copyFromServer = Task.Run(async () =>
                    {
                        try
                        {
                            var buffer = new byte[4096];
                            int bytesRead;
                            while ((bytesRead = await request.InputStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await clientStream.WriteAsync(buffer, 0, bytesRead);
                                await clientStream.FlushAsync();
                            }
                        }
                        catch { }
                    });

                    await Task.WhenAny(copyToServer, copyFromServer);
                    OnLogMessage($"[CONNECT 직접 종료] {targetHost} 연결 종료");
                }
            }
            catch (Exception ex)
            {
                OnLogMessage($"[CONNECT 직접 오류] {targetHost} - 오류: {ex.Message}");
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
                OnLogMessage($"[CONNECT] HTTPS 터널링 요청: {rawUrl} -> {targetHost}");

                // 필터링 체크
                bool shouldProxy = false;
                if (!string.IsNullOrEmpty(targetHost))
                {
                    var testUrl = new Uri($"https://{targetHost}");
                    shouldProxy = ShouldProxyRequest(testUrl);
                    
                    if (!shouldProxy)
                    {
                        // 필터링 대상이 아니면 직접 통과 (패싱)
                        OnLogMessage($"[CONNECT 직접 통과] 도메인 '{targetHost}'이(가) 필터 목록에 없어 직접 통과합니다");
                        await HandleDirectConnectAsync(context, targetHost);
                        return;
                    }
                }

                // CONNECT 요청을 Relay Server로 전달
                var sessionId = Guid.NewGuid().ToString();
                var relayMessage = new RelayMessage
                {
                    Type = "REQUEST",
                    SessionId = sessionId,
                    Method = "CONNECT",
                    Url = $"https://{targetHost}",
                    Headers = new Dictionary<string, string>()
                };

                // WebSocket 연결 상태 확인
                if (_webSocket == null || _webSocket.State != WebSocketState.Open)
                {
                    OnLogMessage($"[CONNECT 실패] WebSocket이 연결되지 않았습니다. 상태: {_webSocket?.State ?? WebSocketState.None}");
                    response.StatusCode = 502;
                    response.StatusDescription = "Bad Gateway - WebSocket not connected";
                    response.Close();
                    return;
                }

                // Relay Server로 전송
                OnLogMessage($"[CONNECT 전송] Relay Server로 CONNECT 요청 전송: {targetHost}");
                try
                {
                    await SendMessageAsync(relayMessage);
                    OnLogMessage($"[CONNECT 전송 성공] Relay Server로 CONNECT 요청 전송 완료: {targetHost}");
                }
                catch (Exception ex)
                {
                    var errorDetails = ex.Message;
                    if (ex.InnerException != null)
                    {
                        errorDetails += $" (내부 오류: {ex.InnerException.Message})";
                    }
                    OnLogMessage($"[CONNECT 전송 실패] Relay Server로 CONNECT 요청 전송 실패: {errorDetails}");
                    response.StatusCode = 502;
                    response.StatusDescription = "Bad Gateway - Failed to send CONNECT request";
                    response.Close();
                    return;
                }

                // 응답 대기
                var tcs = new TaskCompletionSource<RelayMessage>();
                _pendingRequests[sessionId] = tcs;

                try
                {
                    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10));
                    var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

                    if (completedTask == timeoutTask)
                    {
                        if (_pendingRequests.ContainsKey(sessionId))
                        {
                            _pendingRequests.Remove(sessionId);
                        }
                        OnLogMessage($"[CONNECT 타임아웃] CONNECT 요청 타임아웃: {targetHost}");
                        response.StatusCode = 504;
                        response.StatusDescription = "Gateway Timeout";
                        response.Close();
                        return;
                    }

                    RelayMessage relayResponse;
                    try
                    {
                        relayResponse = await tcs.Task;
                    }
                    catch (Exception ex)
                    {
                        if (_pendingRequests.ContainsKey(sessionId))
                        {
                            _pendingRequests.Remove(sessionId);
                        }
                        OnLogMessage($"[CONNECT 응답 수신 오류] 응답 수신 중 오류 발생: {ex.Message}");
                        response.StatusCode = 502;
                        response.StatusDescription = "Bad Gateway - Error receiving CONNECT response";
                        response.Close();
                        return;
                    }

                    if (_pendingRequests.ContainsKey(sessionId))
                    {
                        _pendingRequests.Remove(sessionId);
                    }

                    if (relayResponse.StatusCode == 200)
                    {
                        // CONNECT 성공 - 터널링 시작
                        response.StatusCode = 200;
                        response.StatusDescription = "Connection Established";
                        response.Headers.Add("Connection", "keep-alive");
                        await response.OutputStream.FlushAsync();
                        OnLogMessage($"[CONNECT 성공] HTTPS 터널링 시작: {targetHost}");
                        // 실제 터널링은 복잡하므로 여기서는 연결만 확인
                        response.Close();
                    }
                    else
                    {
                        OnLogMessage($"[CONNECT 실패] Relay Server 응답: {relayResponse.StatusCode}");
                        response.StatusCode = relayResponse.StatusCode ?? 502;
                        response.StatusDescription = relayResponse.Error ?? "Bad Gateway";
                        response.Close();
                    }
                }
                catch (Exception ex)
                {
                    if (_pendingRequests.ContainsKey(sessionId))
                    {
                        _pendingRequests.Remove(sessionId);
                    }
                    OnLogMessage($"[CONNECT 응답 처리 오류] 응답 처리 중 오류 발생: {ex.Message}");
                    try
                    {
                        response.StatusCode = 502;
                        response.StatusDescription = "Bad Gateway - Error processing CONNECT response";
                        response.Close();
                    }
                    catch { }
                }
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
                var errorMsg = $"WebSocket이 연결되지 않았습니다. 현재 상태: {_webSocket?.State}";
                OnLogMessage($"[전송 실패] {errorMsg}");
                throw new InvalidOperationException(errorMsg);
            }

            try
            {
                var json = SerializeMessage(message);
                var bytes = Encoding.UTF8.GetBytes(json);

                OnLogMessage($"[WebSocket 전송] 메시지 전송 시도: {message.Type} (크기: {bytes.Length} bytes)");

                await _webSocket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None
                );

                OnLogMessage($"[WebSocket 전송 성공] 메시지 전송 완료: {message.Type}");
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

