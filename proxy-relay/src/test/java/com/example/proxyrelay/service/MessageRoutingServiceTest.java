package com.example.proxyrelay.service;

import com.example.proxyrelay.dto.ClientType;
import com.example.proxyrelay.dto.RelayMessage;
import com.example.proxyrelay.dto.SessionInfo;
import com.fasterxml.jackson.databind.ObjectMapper;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.springframework.web.reactive.socket.WebSocketMessage;
import org.springframework.web.reactive.socket.WebSocketSession;
import reactor.core.publisher.Mono;
import reactor.test.StepVerifier;

import java.util.ArrayList;
import java.util.List;

import static org.junit.jupiter.api.Assertions.*;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.Mockito.*;

/**
 * MessageRoutingService 테스트
 * 메시지 파싱 및 라우팅 기능을 테스트합니다.
 */
class MessageRoutingServiceTest {
    
    private MessageRoutingService messageRoutingService;
    private SessionService sessionService;
    private WebSocketSession mockSessionA;
    private WebSocketSession mockSessionB;
    
    @BeforeEach
    void setUp() {
        sessionService = new SessionService();
        messageRoutingService = new MessageRoutingService(sessionService);
        
        mockSessionA = mock(WebSocketSession.class);
        mockSessionB = mock(WebSocketSession.class);
        
        when(mockSessionA.getId()).thenReturn("session-a-1");
        when(mockSessionB.getId()).thenReturn("session-b-1");
        when(mockSessionA.isOpen()).thenReturn(true);
        when(mockSessionB.isOpen()).thenReturn(true);
        
        sessionService.registerSession(mockSessionA, ClientType.CLIENT_A, "token");
        sessionService.registerSession(mockSessionB, ClientType.CLIENT_B, "token");
        sessionService.mapSessions("session-a-1", "session-b-1");
    }
    
    /**
     * 검증: 유효한 JSON 메시지가 올바르게 파싱되어야 함
     * 목적: JSON 문자열을 RelayMessage 객체로 변환하는 기능 확인
     */
    @Test
    void parseMessage_ValidJson_ReturnsRelayMessage() {
        String json = """
            {
                "type": "REQUEST",
                "sessionId": "test-session",
                "method": "GET",
                "url": "http://internal/api"
            }
            """;
        
        RelayMessage message = messageRoutingService.parseMessage(json);
        
        assertNotNull(message);
        assertEquals(RelayMessage.MessageType.REQUEST, message.getType());
        assertEquals("test-session", message.getSessionId());
        assertEquals("GET", message.getMethod());
        assertEquals("http://internal/api", message.getUrl());
    }
    
    /**
     * 검증: 잘못된 JSON 형식이 주어지면 null을 반환해야 함
     * 목적: 잘못된 메시지 포맷에 대한 안전한 처리 확인
     */
    @Test
    void parseMessage_InvalidJson_ReturnsNull() {
        String invalidJson = "{ invalid json }";
        
        RelayMessage message = messageRoutingService.parseMessage(invalidJson);
        
        assertNull(message);
    }
    
    /**
     * 검증: 빈 JSON 문자열이 주어지면 null을 반환해야 함
     * 목적: 빈 입력에 대한 안전한 처리 확인
     */
    @Test
    void parseMessage_EmptyJson_ReturnsNull() {
        RelayMessage message = messageRoutingService.parseMessage("");
        assertNull(message);
    }
    
    /**
     * 검증: Client A의 요청이 매핑된 Client B로 올바르게 전달되어야 함
     * 목적: 외부 프록시(Client A)에서 내부 에이전트(Client B)로의 메시지 라우팅 확인
     */
    @Test
    void routeRequestToAgent_ValidMapping_SendsMessage() {
        List<WebSocketMessage> sentMessages = new ArrayList<>();
        when(mockSessionB.send(any())).thenAnswer(invocation -> {
            Mono<WebSocketMessage> messageMono = invocation.getArgument(0);
            messageMono.subscribe(sentMessages::add);
            return Mono.empty();
        });
        
        RelayMessage request = new RelayMessage();
        request.setType(RelayMessage.MessageType.REQUEST);
        request.setMethod("GET");
        request.setUrl("http://internal/api");
        
        Mono<Void> result = messageRoutingService.routeRequestToAgent("session-a-1", request);
        
        StepVerifier.create(result)
            .verifyComplete();
        
        verify(mockSessionB, times(1)).send(any());
    }
    
    /**
     * 검증: 매핑된 Client B가 없으면 Client A에게 에러 응답을 보내야 함
     * 목적: 에이전트가 없을 때 프록시에게 적절한 에러 메시지 전달 확인
     */
    @Test
    void routeRequestToAgent_NoMapping_SendsErrorResponse() {
        List<WebSocketMessage> sentMessages = new ArrayList<>();
        when(mockSessionA.send(any())).thenAnswer(invocation -> {
            Mono<WebSocketMessage> messageMono = invocation.getArgument(0);
            messageMono.subscribe(sentMessages::add);
            return Mono.empty();
        });
        
        // 매핑 제거
        sessionService.removeSession("session-b-1");
        
        RelayMessage request = new RelayMessage();
        request.setType(RelayMessage.MessageType.REQUEST);
        
        Mono<Void> result = messageRoutingService.routeRequestToAgent("session-a-1", request);
        
        StepVerifier.create(result)
            .verifyComplete();
        
        verify(mockSessionA, times(1)).send(any());
    }
    
    /**
     * 검증: Client B의 응답이 매핑된 Client A로 올바르게 전달되어야 함
     * 목적: 내부 에이전트(Client B)에서 외부 프록시(Client A)로의 응답 라우팅 확인
     */
    @Test
    void routeResponseToClient_ValidMapping_SendsMessage() {
        List<WebSocketMessage> sentMessages = new ArrayList<>();
        when(mockSessionA.send(any())).thenAnswer(invocation -> {
            Mono<WebSocketMessage> messageMono = invocation.getArgument(0);
            messageMono.subscribe(sentMessages::add);
            return Mono.empty();
        });
        
        RelayMessage response = new RelayMessage();
        response.setType(RelayMessage.MessageType.RESPONSE);
        response.setStatusCode(200);
        
        Mono<Void> result = messageRoutingService.routeResponseToClient("session-b-1", response);
        
        StepVerifier.create(result)
            .verifyComplete();
        
        verify(mockSessionA, times(1)).send(any());
    }
    
    /**
     * 검증: 매핑된 Client A가 없으면 메시지를 전송하지 않아야 함
     * 목적: 프록시가 없을 때 에이전트의 응답이 무시되는지 확인
     */
    @Test
    void routeResponseToClient_NoMapping_DoesNotSend() {
        // 매핑 제거
        sessionService.removeSession("session-a-1");
        
        RelayMessage response = new RelayMessage();
        response.setType(RelayMessage.MessageType.RESPONSE);
        
        Mono<Void> result = messageRoutingService.routeResponseToClient("session-b-1", response);
        
        StepVerifier.create(result)
            .verifyComplete();
        
        verify(mockSessionA, never()).send(any());
    }
}

