package com.example.proxyrelay.handler;

import com.example.proxyrelay.dto.ClientType;
import com.example.proxyrelay.dto.RelayMessage;
import com.example.proxyrelay.service.AuthService;
import com.example.proxyrelay.service.MessageRoutingService;
import com.example.proxyrelay.service.SessionService;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.springframework.http.HttpHeaders;
import org.springframework.web.reactive.socket.HandshakeInfo;
import org.springframework.web.reactive.socket.WebSocketSession;

import java.net.URI;
import java.security.Principal;
import reactor.core.publisher.Mono;
import reactor.core.publisher.Mono;

import static org.junit.jupiter.api.Assertions.*;
import static org.mockito.Mockito.*;

/**
 * RelayWebSocketHandler 단위 테스트
 * 핸들러의 개별 메서드와 로직을 독립적으로 테스트합니다.
 * Mock을 사용하여 의존성을 격리합니다.
 */
class RelayWebSocketHandlerUnitTest {
    
    private RelayWebSocketHandler handler;
    private SessionService mockSessionService;
    private AuthService mockAuthService;
    private MessageRoutingService mockMessageRoutingService;
    private WebSocketSession mockSession;
    private Principal mockPrincipal;
    
    @BeforeEach
    void setUp() {
        mockSessionService = mock(SessionService.class);
        mockAuthService = mock(AuthService.class);
        mockMessageRoutingService = mock(MessageRoutingService.class);
        
        handler = new RelayWebSocketHandler(
            mockSessionService,
            mockAuthService,
            mockMessageRoutingService
        );
        
        mockSession = mock(WebSocketSession.class);
        when(mockSession.getId()).thenReturn("test-session-1");
        
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
     * 검증: identifyClientType 메서드가 Query Parameter에서 type=A를 올바르게 식별
     * 목적: 클라이언트 타입 식별 로직의 단위 테스트
     */
    @Test
    void identifyClientType_QueryParameterTypeA_ReturnsClientA() throws Exception {
        // Given
        URI uri = new URI("ws://localhost:8080/relay?type=A&token=test");
        HandshakeInfo handshakeInfo = createHandshakeInfo(uri, new HttpHeaders());
        when(mockSession.getHandshakeInfo()).thenReturn(handshakeInfo);
        
        // When - Reflection을 사용하여 private 메서드 호출
        var method = RelayWebSocketHandler.class.getDeclaredMethod(
            "identifyClientType", WebSocketSession.class
        );
        method.setAccessible(true);
        ClientType result = (ClientType) method.invoke(handler, mockSession);
        
        // Then
        assertEquals(ClientType.CLIENT_A, result);
    }
    
    /**
     * 검증: identifyClientType 메서드가 Query Parameter에서 type=B를 올바르게 식별
     * 목적: Client B 타입 식별 로직 확인
     */
    @Test
    void identifyClientType_QueryParameterTypeB_ReturnsClientB() throws Exception {
        // Given
        URI uri = new URI("ws://localhost:8080/relay?type=B&token=test");
        HandshakeInfo handshakeInfo = createHandshakeInfo(uri, new HttpHeaders());
        when(mockSession.getHandshakeInfo()).thenReturn(handshakeInfo);
        
        // When
        var method = RelayWebSocketHandler.class.getDeclaredMethod(
            "identifyClientType", WebSocketSession.class
        );
        method.setAccessible(true);
        ClientType result = (ClientType) method.invoke(handler, mockSession);
        
        // Then
        assertEquals(ClientType.CLIENT_B, result);
    }
    
    /**
     * 검증: identifyClientType 메서드가 User-Agent 헤더로 Client B를 식별
     * 목적: 헤더 기반 타입 식별 로직 확인
     */
    @Test
    void identifyClientType_UserAgentContainsAgent_ReturnsClientB() throws Exception {
        // Given
        URI uri = new URI("ws://localhost:8080/relay?token=test");
        HttpHeaders headers = new HttpHeaders();
        headers.add("User-Agent", "MyAgent/1.0");
        HandshakeInfo handshakeInfo = createHandshakeInfo(uri, headers);
        when(mockSession.getHandshakeInfo()).thenReturn(handshakeInfo);
        
        // When
        var method = RelayWebSocketHandler.class.getDeclaredMethod(
            "identifyClientType", WebSocketSession.class
        );
        method.setAccessible(true);
        ClientType result = (ClientType) method.invoke(handler, mockSession);
        
        // Then
        assertEquals(ClientType.CLIENT_B, result);
    }
    
    /**
     * 검증: identifyClientType 메서드가 기본값으로 Client A를 반환
     * 목적: 타입을 식별할 수 없을 때의 기본값 확인
     */
    @Test
    void identifyClientType_NoTypeInfo_ReturnsClientAAsDefault() throws Exception {
        // Given
        URI uri = new URI("ws://localhost:8080/relay?token=test");
        HandshakeInfo handshakeInfo = createHandshakeInfo(uri, new HttpHeaders());
        when(mockSession.getHandshakeInfo()).thenReturn(handshakeInfo);
        
        // When
        var method = RelayWebSocketHandler.class.getDeclaredMethod(
            "identifyClientType", WebSocketSession.class
        );
        method.setAccessible(true);
        ClientType result = (ClientType) method.invoke(handler, mockSession);
        
        // Then
        assertEquals(ClientType.CLIENT_A, result);
    }
    
    /**
     * 검증: extractAccessToken 메서드가 Query Parameter에서 토큰을 추출
     * 목적: 토큰 추출 로직의 단위 테스트
     */
    @Test
    void extractAccessToken_FromQueryParameter_ReturnsToken() throws Exception {
        // Given
        URI uri = new URI("ws://localhost:8080/relay?type=A&token=test-token-123");
        HandshakeInfo handshakeInfo = createHandshakeInfo(uri, new HttpHeaders());
        when(mockSession.getHandshakeInfo()).thenReturn(handshakeInfo);
        when(mockAuthService.extractToken("type=A&token=test-token-123"))
            .thenReturn("test-token-123");
        
        // When
        var method = RelayWebSocketHandler.class.getDeclaredMethod(
            "extractAccessToken", WebSocketSession.class
        );
        method.setAccessible(true);
        String result = (String) method.invoke(handler, mockSession);
        
        // Then
        assertEquals("test-token-123", result);
        verify(mockAuthService, times(1)).extractToken(anyString());
    }
    
    /**
     * 검증: extractAccessToken 메서드가 Authorization 헤더에서 토큰을 추출
     * 목적: 헤더 기반 토큰 추출 로직 확인
     */
    @Test
    void extractAccessToken_FromAuthorizationHeader_ReturnsToken() throws Exception {
        // Given
        URI uri = new URI("ws://localhost:8080/relay?type=A");
        HttpHeaders headers = new HttpHeaders();
        headers.add("Authorization", "Bearer bearer-token-123");
        HandshakeInfo handshakeInfo = createHandshakeInfo(uri, headers);
        when(mockSession.getHandshakeInfo()).thenReturn(handshakeInfo);
        when(mockAuthService.extractToken("type=A")).thenReturn(null);
        
        // When
        var method = RelayWebSocketHandler.class.getDeclaredMethod(
            "extractAccessToken", WebSocketSession.class
        );
        method.setAccessible(true);
        String result = (String) method.invoke(handler, mockSession);
        
        // Then
        assertEquals("bearer-token-123", result);
    }
    
    /**
     * 검증: extractAccessToken 메서드가 토큰을 찾지 못하면 null 반환
     * 목적: 토큰이 없을 때의 처리 확인
     */
    @Test
    void extractAccessToken_NoToken_ReturnsNull() throws Exception {
        // Given
        URI uri = new URI("ws://localhost:8080/relay?type=A");
        HandshakeInfo handshakeInfo = createHandshakeInfo(uri, new HttpHeaders());
        when(mockSession.getHandshakeInfo()).thenReturn(handshakeInfo);
        when(mockAuthService.extractToken("type=A")).thenReturn(null);
        
        // When
        var method = RelayWebSocketHandler.class.getDeclaredMethod(
            "extractAccessToken", WebSocketSession.class
        );
        method.setAccessible(true);
        String result = (String) method.invoke(handler, mockSession);
        
        // Then
        assertNull(result);
    }
}

