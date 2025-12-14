package com.example.proxyrelay.handler;

import com.example.proxyrelay.dto.ClientType;
import com.example.proxyrelay.dto.RelayMessage;
import com.example.proxyrelay.service.AuthService;
import com.example.proxyrelay.service.MessageRoutingService;
import com.example.proxyrelay.service.SessionService;
import com.fasterxml.jackson.databind.ObjectMapper;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.springframework.http.HttpHeaders;
import org.springframework.test.util.ReflectionTestUtils;
import org.springframework.web.reactive.socket.CloseStatus;
import org.springframework.web.reactive.socket.HandshakeInfo;
import org.springframework.web.reactive.socket.WebSocketMessage;
import org.springframework.web.reactive.socket.WebSocketSession;
import reactor.core.publisher.Flux;
import reactor.core.publisher.Mono;
import reactor.test.StepVerifier;

import java.net.URI;
import java.security.Principal;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;

import static org.junit.jupiter.api.Assertions.*;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.Mockito.*;

/**
 * RelayWebSocketHandler 통합 테스트
 * WebSocket 연결 처리, 인증, 메시지 라우팅의 전체 흐름을 테스트합니다.
 * 
 * 참고: 순수한 단위 테스트는 RelayWebSocketHandlerUnitTest를 참고하세요.
 * 이 테스트는 실제 서비스 인스턴스를 사용하여 통합 테스트를 수행합니다.
 */
class RelayWebSocketHandlerTest {
    
    private RelayWebSocketHandler handler;
    private SessionService sessionService;
    private AuthService authService;
    private MessageRoutingService messageRoutingService;
    private WebSocketSession mockSession;
    private ObjectMapper objectMapper;
    private Principal mockPrincipal;
    
    @BeforeEach
    void setUp() {
        sessionService = new SessionService();
        authService = new AuthService();
        // @Value가 테스트에서 작동하지 않으므로 ReflectionTestUtils로 설정
        ReflectionTestUtils.setField(authService, "validAccessToken", "default-token-change-in-production");
        messageRoutingService = new MessageRoutingService(sessionService);
        handler = new RelayWebSocketHandler(sessionService, authService, messageRoutingService);
        objectMapper = new ObjectMapper();
        
        mockSession = mock(WebSocketSession.class);
        when(mockSession.getId()).thenReturn("test-session-1");
        when(mockSession.isOpen()).thenReturn(true);
        
        // Mock Principal 생성
        mockPrincipal = mock(Principal.class);
        when(mockPrincipal.getName()).thenReturn("test-user");
    }
    
    /**
     * HandshakeInfo 생성 헬퍼 메서드
     */
    private HandshakeInfo createHandshakeInfo(URI uri, HttpHeaders headers) {
        return new HandshakeInfo(uri, headers, Mono.just(mockPrincipal), null);
    }
    
    /**
     * 검증: 유효한 토큰과 클라이언트 타입으로 연결 시 세션이 등록되어야 함
     * 목적: 정상적인 WebSocket 연결이 성공적으로 처리되는지 확인
     */
    @Test
    void handle_ValidTokenAndClientType_RegistersSession() throws Exception {
        // Given
        URI uri = new URI("ws://localhost:8080/relay?type=A&token=default-token-change-in-production");
        HandshakeInfo handshakeInfo = createHandshakeInfo(uri, new HttpHeaders());
        when(mockSession.getHandshakeInfo()).thenReturn(handshakeInfo);
        when(mockSession.receive()).thenReturn(Flux.empty());
        when(mockSession.send(any())).thenReturn(Mono.empty());
        
        // When
        Mono<Void> result = handler.handle(mockSession);
        
        // Then
        StepVerifier.create(result)
            .verifyComplete();
        
        verify(mockSession, never()).close(any(CloseStatus.class));
    }
    
    /**
     * 검증: 잘못된 토큰으로 연결 시도 시 연결이 거부되어야 함
     * 목적: 무효한 토큰에 대한 보안 검증 확인
     */
    @Test
    void handle_InvalidToken_ClosesConnection() throws Exception {
        // Given
        URI uri = new URI("ws://localhost:8080/relay?type=A&token=invalid-token");
        HandshakeInfo handshakeInfo = createHandshakeInfo(uri, new HttpHeaders());
        when(mockSession.getHandshakeInfo()).thenReturn(handshakeInfo);
        when(mockSession.close(any(CloseStatus.class))).thenReturn(Mono.empty());
        
        // When
        Mono<Void> result = handler.handle(mockSession);
        
        // Then
        StepVerifier.create(result)
            .verifyComplete();
        
        verify(mockSession, times(1)).close(argThat(status -> 
            status.getCode() == CloseStatus.POLICY_VIOLATION.getCode()
        ));
    }
    
    /**
     * 검증: 토큰 없이 연결 시도 시 연결이 거부되어야 함
     * 목적: 토큰이 필수임을 확인하고 인증 없이 접근 차단 확인
     */
    @Test
    void handle_NoToken_ClosesConnection() throws Exception {
        // Given
        URI uri = new URI("ws://localhost:8080/relay?type=A");
        HandshakeInfo handshakeInfo = createHandshakeInfo(uri, new HttpHeaders());
        when(mockSession.getHandshakeInfo()).thenReturn(handshakeInfo);
        when(mockSession.close(any(CloseStatus.class))).thenReturn(Mono.empty());
        
        // When
        Mono<Void> result = handler.handle(mockSession);
        
        // Then
        StepVerifier.create(result)
            .verifyComplete();
        
        verify(mockSession, times(1)).close(any(CloseStatus.class));
    }
    
    /**
     * 검증: Authorization 헤더에 Bearer 토큰이 있으면 인증이 성공해야 함
     * 목적: Query Parameter 외에 Header를 통한 토큰 전달 방식 지원 확인
     */
    @Test
    void handle_TokenInHeader_ValidatesSuccessfully() throws Exception {
        // Given
        URI uri = new URI("ws://localhost:8080/relay?type=A");
        HttpHeaders headers = new HttpHeaders();
        headers.add("Authorization", "Bearer default-token-change-in-production");
        HandshakeInfo handshakeInfo = createHandshakeInfo(uri, headers);
        when(mockSession.getHandshakeInfo()).thenReturn(handshakeInfo);
        when(mockSession.receive()).thenReturn(Flux.empty());
        when(mockSession.send(any())).thenReturn(Mono.empty());
        
        // When
        Mono<Void> result = handler.handle(mockSession);
        
        // Then
        StepVerifier.create(result)
            .verifyComplete();
        
        verify(mockSession, never()).close(any(CloseStatus.class));
    }
    
    /**
     * 검증: type=A로 연결 시 Client A로 등록되어야 함
     * 목적: 외부 프록시(Client A) 타입 식별 및 등록 확인
     */
    @Test
    void handle_ClientTypeA_RegistersAsClientA() throws Exception {
        // Given
        URI uri = new URI("ws://localhost:8080/relay?type=A&token=default-token-change-in-production");
        HandshakeInfo handshakeInfo = createHandshakeInfo(uri, new HttpHeaders());
        when(mockSession.getHandshakeInfo()).thenReturn(handshakeInfo);
        // receive()가 empty이면 즉시 완료되어 doFinally에서 세션이 제거됨
        // 따라서 receive()를 never()로 설정하여 연결을 유지
        when(mockSession.receive()).thenReturn(Flux.never());
        when(mockSession.send(any())).thenReturn(Mono.empty());
        when(mockSession.textMessage(anyString())).thenReturn(mock(WebSocketMessage.class));
        
        // When - 비동기로 시작
        handler.handle(mockSession).subscribe();
        
        // Then - 세션이 등록되었는지 확인 (doFinally 전에)
        Thread.sleep(100); // 등록 대기
        assertNotNull(sessionService.getClientA("test-session-1"), "Client A session should be registered");
        assertNull(sessionService.getClientB("test-session-1"), "Client B session should not exist");
    }
    
    /**
     * 검증: type=B로 연결 시 Client B로 등록되어야 함
     * 목적: 내부 에이전트(Client B) 타입 식별 및 등록 확인
     */
    @Test
    void handle_ClientTypeB_RegistersAsClientB() throws Exception {
        // Given
        URI uri = new URI("ws://localhost:8080/relay?type=B&token=default-token-change-in-production");
        HandshakeInfo handshakeInfo = createHandshakeInfo(uri, new HttpHeaders());
        when(mockSession.getHandshakeInfo()).thenReturn(handshakeInfo);
        // receive()를 never()로 설정하여 연결 유지 (doFinally 방지)
        when(mockSession.receive()).thenReturn(Flux.never());
        when(mockSession.send(any())).thenReturn(Mono.empty());
        when(mockSession.textMessage(anyString())).thenReturn(mock(WebSocketMessage.class));
        
        // When
        handler.handle(mockSession).subscribe();
        
        // Then
        Thread.sleep(100); // 등록 대기
        assertNull(sessionService.getClientA("test-session-1"), "Client A session should not exist");
        assertNotNull(sessionService.getClientB("test-session-1"), "Client B session should be registered");
    }
    
    /**
     * 검증: Client A가 REQUEST 메시지를 보내면 매핑된 Client B로 전달되어야 함
     * 목적: 요청 메시지의 전체 라우팅 흐름 확인 (A → Relay → B)
     */
    @Test
    void handle_RequestMessage_RoutesToAgent() throws Exception {
        // Given
        // Client A를 먼저 연결 (매핑을 위해)
        URI uri = new URI("ws://localhost:8080/relay?type=A&token=default-token-change-in-production");
        HandshakeInfo handshakeInfo = createHandshakeInfo(uri, new HttpHeaders());
        when(mockSession.getHandshakeInfo()).thenReturn(handshakeInfo);
        
        RelayMessage requestMessage = new RelayMessage();
        requestMessage.setType(RelayMessage.MessageType.REQUEST);
        requestMessage.setMethod("GET");
        requestMessage.setUrl("http://internal/api");
        
        String requestJson = objectMapper.writeValueAsString(requestMessage);
        WebSocketMessage wsMessage = mock(WebSocketMessage.class);
        when(wsMessage.getPayloadAsText()).thenReturn(requestJson);
        
        // Client B 세션 등록
        WebSocketSession mockSessionB = mock(WebSocketSession.class);
        when(mockSessionB.getId()).thenReturn("session-b-1");
        when(mockSessionB.isOpen()).thenReturn(true);
        URI uriB = new URI("ws://localhost:8080/relay?type=B&token=default-token-change-in-production");
        HandshakeInfo handshakeInfoB = createHandshakeInfo(uriB, new HttpHeaders());
        when(mockSessionB.getHandshakeInfo()).thenReturn(handshakeInfoB);
        when(mockSessionB.receive()).thenReturn(Flux.never()); // 연결 유지
        when(mockSessionB.send(any())).thenReturn(Mono.empty());
        when(mockSessionB.textMessage(anyString())).thenReturn(mock(WebSocketMessage.class));
        
        // Client A 먼저 연결 (메시지는 200ms 후에 보냄)
        when(mockSession.receive()).thenReturn(
            Flux.defer(() -> 
                Mono.delay(java.time.Duration.ofMillis(200))
                    .thenMany(Flux.just(wsMessage))
                    .concatWith(Flux.never())
            )
        );
        when(mockSession.send(any())).thenReturn(Mono.empty());
        when(mockSession.textMessage(anyString())).thenReturn(mock(WebSocketMessage.class));
        
        handler.handle(mockSession).subscribe();
        Thread.sleep(100); // 등록 대기
        
        // Client B 연결 (이때 Client A와 매핑됨)
        handler.handle(mockSessionB).subscribe();
        Thread.sleep(100); // 매핑 대기
        
        // When - 메시지 처리 대기 (200ms 지연 + 처리 시간)
        Thread.sleep(400);
        
        // Then - Client B로 메시지가 전달되었는지 확인
        verify(mockSessionB, atLeastOnce()).send(any());
    }
    
    /**
     * 검증: PING 메시지를 받으면 PONG 응답을 보내야 함
     * 목적: 연결 유지를 위한 Keep-Alive 메커니즘 확인
     */
    @Test
    void handle_PingMessage_RespondsWithPong() throws Exception {
        // Given
        URI uri = new URI("ws://localhost:8080/relay?type=A&token=default-token-change-in-production");
        HandshakeInfo handshakeInfo = createHandshakeInfo(uri, new HttpHeaders());
        when(mockSession.getHandshakeInfo()).thenReturn(handshakeInfo);
        
        RelayMessage pingMessage = new RelayMessage();
        pingMessage.setType(RelayMessage.MessageType.PING);
        String pingJson = objectMapper.writeValueAsString(pingMessage);
        WebSocketMessage wsMessage = mock(WebSocketMessage.class);
        when(wsMessage.getPayloadAsText()).thenReturn(pingJson);
        
        WebSocketMessage pongMessage = mock(WebSocketMessage.class);
        when(mockSession.textMessage(anyString())).thenReturn(pongMessage);
        when(mockSession.receive()).thenReturn(Flux.just(wsMessage));
        when(mockSession.send(any())).thenReturn(Mono.empty());
        
        // When
        handler.handle(mockSession).block();
        
        // Then - PONG 응답 전송 확인
        verify(mockSession, atLeastOnce()).send(any());
    }
}

