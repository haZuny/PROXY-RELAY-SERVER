package com.example.proxyrelay.handler;

import com.example.proxyrelay.dto.ClientType;
import com.example.proxyrelay.dto.RelayMessage;
import com.example.proxyrelay.dto.SessionInfo;
import com.example.proxyrelay.service.AuthService;
import com.example.proxyrelay.service.MessageRoutingService;
import com.example.proxyrelay.service.SessionService;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.stereotype.Component;
import org.springframework.web.reactive.socket.CloseStatus;
import org.springframework.web.reactive.socket.WebSocketHandler;
import org.springframework.web.reactive.socket.WebSocketMessage;
import org.springframework.web.reactive.socket.WebSocketSession;
import reactor.core.publisher.Mono;

import java.net.URI;

/**
 * Relay Server의 핵심 WebSocket 핸들러
 * Client A (외부 프록시)와 Client B (내부 에이전트) 간의 메시지 중계
 */
@Component
public class RelayWebSocketHandler implements WebSocketHandler {
    
    private static final Logger logger = LoggerFactory.getLogger(RelayWebSocketHandler.class);
    
    private final SessionService sessionService;
    private final AuthService authService;
    private final MessageRoutingService messageRoutingService;
    
    public RelayWebSocketHandler(
            SessionService sessionService,
            AuthService authService,
            MessageRoutingService messageRoutingService) {
        this.sessionService = sessionService;
        this.authService = authService;
        this.messageRoutingService = messageRoutingService;
    }
    
    @Override
    public Mono<Void> handle(WebSocketSession session) {
        logger.info("New WebSocket connection: {}", session.getId());
        
        // 1. 인증 및 클라이언트 타입 식별
        ClientType clientType = identifyClientType(session);
        String accessToken = extractAccessToken(session);
        
        if (clientType == null || !authService.validateToken(accessToken)) {
            logger.warn("Invalid connection attempt from session: {}", session.getId());
            return session.close(CloseStatus.POLICY_VIOLATION.withReason("Invalid token"));
        }
        
        // 2. Client A인 경우 Client B 확인 및 매핑 (Client B가 먼저 연결되어 있어야 함)
        // Client B는 먼저 연결되어 대기하고, Client A가 나중에 연결되면 매핑됨
        SessionInfo availableClientB = null;
        if (clientType == ClientType.CLIENT_A) {
            availableClientB = sessionService.findAvailableClientB();
            if (availableClientB == null) {
                logger.warn("No available Client B found for Client A {}. Connection rejected.", session.getId());
                // 에러 메시지 전송 후 연결 종료
                return sendErrorAndClose(session, "No active agent available. Please ensure Client B is connected first.");
            }
        }
        
        // 3. 세션 등록
        sessionService.registerSession(session, clientType, accessToken);
        logger.info("Session registered: {} as {}", session.getId(), clientType);
        
        // 4. 세션 매핑 (Client A와 Client B 연결)
        if (clientType == ClientType.CLIENT_A && availableClientB != null) {
            sessionService.mapSessions(session.getId(), availableClientB.getSession().getId());
            logger.info("Mapped Client A {} to Client B {}", 
                session.getId(), availableClientB.getSession().getId());
        }
        
        // 4. 메시지 수신 처리
        return session.receive()
            .map(WebSocketMessage::getPayloadAsText)
            .filter(messageText -> {
                // 빈 메시지 필터링
                if (messageText == null || messageText.trim().isEmpty()) {
                    logger.debug("Empty message received from session: {}, ignoring", session.getId());
                    return false;
                }
                return true;
            })
            .flatMap(message -> handleMessage(session, message))
            .then()
            .doFinally(signalType -> {
                logger.info("Connection closed: {} - {}", session.getId(), signalType);
                sessionService.removeSession(session.getId());
            });
    }
    
    /**
     * 클라이언트 타입 식별
     * Query Parameter에서 type=A 또는 type=B로 구분
     */
    private ClientType identifyClientType(WebSocketSession session) {
        URI uri = session.getHandshakeInfo().getUri();
        String query = uri.getQuery();
        
        if (query != null) {
            if (query.contains("type=A") || query.contains("type=CLIENT_A")) {
                return ClientType.CLIENT_A;
            } else if (query.contains("type=B") || query.contains("type=CLIENT_B")) {
                return ClientType.CLIENT_B;
            }
        }
        
        // 기본값: 헤더에서 확인
        String userAgent = session.getHandshakeInfo().getHeaders().getFirst("User-Agent");
        if (userAgent != null && userAgent.contains("Agent")) {
            return ClientType.CLIENT_B;
        }
        
        return ClientType.CLIENT_A; // 기본값
    }
    
    /**
     * Access Token 추출
     */
    private String extractAccessToken(WebSocketSession session) {
        URI uri = session.getHandshakeInfo().getUri();
        String query = uri.getQuery();
        
        if (query != null) {
            String token = authService.extractToken(query);
            if (token != null) {
                return token;
            }
        }
        
        // Header에서 확인
        String token = session.getHandshakeInfo().getHeaders().getFirst("Authorization");
        if (token != null && token.startsWith("Bearer ")) {
            return token.substring(7);
        }
        
        return null;
    }
    
    /**
     * 사용 가능한 Client A 찾기
     */
    private SessionInfo findAvailableClientA() {
        // 매핑되지 않은 첫 번째 활성 Client A 반환
        // 실제로는 라운드로빈, 부하 분산 등 고려 가능
        return sessionService.findAvailableClientA();
    }
    
    /**
     * 메시지 처리
     */
    private Mono<Void> handleMessage(WebSocketSession session, String messageText) {
        try {
            RelayMessage message = messageRoutingService.parseMessage(messageText);
            if (message == null) {
                logger.warn("Invalid message format from session: {}", session.getId());
                return Mono.empty();
            }
            
            SessionInfo sessionInfo = sessionService.getClientA(session.getId());
            if (sessionInfo == null) {
                sessionInfo = sessionService.getClientB(session.getId());
            }
            
            if (sessionInfo == null) {
                logger.warn("Session not found: {}", session.getId());
                return Mono.empty();
            }
            
            // PING/PONG 처리
            if (message.getType() == RelayMessage.MessageType.PING) {
                return handlePing(session);
            }
            
            // 요청/응답 라우팅
            if (sessionInfo.getClientType() == ClientType.CLIENT_A) {
                // Client A로부터 요청 → Client B로 전달
                if (message.getType() == RelayMessage.MessageType.REQUEST) {
                    return messageRoutingService.routeRequestToAgent(session.getId(), message);
                }
            } else {
                // Client B로부터 응답 → Client A로 전달
                if (message.getType() == RelayMessage.MessageType.RESPONSE) {
                    return messageRoutingService.routeResponseToClient(session.getId(), message);
                }
            }
            
            return Mono.empty();
        } catch (Exception e) {
            logger.error("Error handling message from session: {}", session.getId(), e);
            return Mono.empty();
        }
    }
    
    /**
     * PING 처리
     */
    private Mono<Void> handlePing(WebSocketSession session) {
        try {
            RelayMessage pong = new RelayMessage();
            pong.setType(RelayMessage.MessageType.PONG);
            String json = new com.fasterxml.jackson.databind.ObjectMapper()
                .writeValueAsString(pong);
            WebSocketMessage wsMessage = session.textMessage(json);
            return session.send(Mono.just(wsMessage));
        } catch (Exception e) {
            logger.error("Error sending PONG", e);
            return Mono.empty();
        }
    }
    
    /**
     * 에러 메시지 전송 후 연결 종료
     */
    private Mono<Void> sendErrorAndClose(WebSocketSession session, String errorMessage) {
        try {
            RelayMessage errorResponse = new RelayMessage();
            errorResponse.setType(RelayMessage.MessageType.RESPONSE);
            errorResponse.setStatusCode(503);
            errorResponse.setError(errorMessage);
            
            String json = new com.fasterxml.jackson.databind.ObjectMapper()
                .writeValueAsString(errorResponse);
            WebSocketMessage wsMessage = session.textMessage(json);
            
            // 에러 메시지 전송 후 연결 종료
            return session.send(Mono.just(wsMessage))
                .then(session.close(CloseStatus.SERVER_ERROR.withReason(errorMessage)));
        } catch (Exception e) {
            logger.error("Error sending error message and closing connection", e);
            return session.close(CloseStatus.SERVER_ERROR.withReason(errorMessage));
        }
    }
}

