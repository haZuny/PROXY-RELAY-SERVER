package com.example.proxyrelay.service;

import com.example.proxyrelay.dto.RelayMessage;
import com.example.proxyrelay.dto.SessionInfo;
import com.fasterxml.jackson.databind.ObjectMapper;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.stereotype.Service;
import org.springframework.web.reactive.socket.WebSocketMessage;
import org.springframework.web.reactive.socket.WebSocketSession;
import reactor.core.publisher.Mono;

/**
 * 메시지 라우팅 서비스
 * Client A와 Client B 간의 메시지 전달
 */
@Service
public class MessageRoutingService {
    
    private static final Logger logger = LoggerFactory.getLogger(MessageRoutingService.class);
    private final ObjectMapper objectMapper = new ObjectMapper();
    private final SessionService sessionService;
    
    public MessageRoutingService(SessionService sessionService) {
        this.sessionService = sessionService;
    }
    
    /**
     * Client A로부터 받은 요청을 Client B로 전달
     */
    public Mono<Void> routeRequestToAgent(String clientASessionId, RelayMessage message) {
        SessionInfo clientB = sessionService.getMappedClientB(clientASessionId);
        
        if (clientB == null || !clientB.isActive()) {
            logger.warn("No active Client B found for session: {}", clientASessionId);
            return sendErrorResponse(clientASessionId, message.getSessionId(), "No active agent available");
        }
        
        try {
            // 요청 메시지에 sessionId가 없으면 생성 (요청-응답 매칭용)
            if (message.getSessionId() == null || message.getSessionId().isEmpty()) {
                message.setSessionId(java.util.UUID.randomUUID().toString());
            }
            
            String jsonMessage = objectMapper.writeValueAsString(message);
            WebSocketMessage wsMessage = clientB.getSession().textMessage(jsonMessage);
            
            if (wsMessage == null) {
                logger.error("Failed to create WebSocket message for Client B {}", clientB.getSession().getId());
                return sendErrorResponse(clientASessionId, message.getSessionId(), "Failed to create message");
            }
            
            logger.info("Routing request from Client A {} to Client B {} (sessionId: {}, method: {}, url: {})", 
                clientASessionId, clientB.getSession().getId(), 
                message.getSessionId(), message.getMethod(), message.getUrl());
            
            return clientB.getSession().send(Mono.just(wsMessage))
                .doOnSuccess(v -> logger.debug("Successfully routed request to Client B {} (sessionId: {})", 
                    clientB.getSession().getId(), message.getSessionId()))
                .doOnError(e -> {
                    logger.error("Error sending message to Client B {} (sessionId: {})", 
                        clientB.getSession().getId(), message.getSessionId(), e);
                    // 전송 실패 시 에러 응답 전송
                    sendErrorResponse(clientASessionId, message.getSessionId(), 
                        "Failed to send request to agent: " + e.getMessage()).subscribe();
                })
                .doOnCancel(() -> {
                    logger.warn("Request routing cancelled for Client A {} (sessionId: {})", 
                        clientASessionId, message.getSessionId());
                    sendErrorResponse(clientASessionId, message.getSessionId(), 
                        "Request cancelled").subscribe();
                });
        } catch (Exception e) {
            logger.error("Error routing message to Client B (sessionId: {})", 
                message.getSessionId(), e);
            return sendErrorResponse(clientASessionId, message.getSessionId(), 
                "Routing error: " + e.getMessage());
        }
    }
    
    /**
     * Client B로부터 받은 응답을 Client A로 전달
     */
    public Mono<Void> routeResponseToClient(String clientBSessionId, RelayMessage message) {
        SessionInfo clientA = sessionService.getMappedClientA(clientBSessionId);
        
        if (clientA == null || !clientA.isActive()) {
            logger.warn("No active Client A found for session: {} (response sessionId: {})", 
                clientBSessionId, message.getSessionId());
            return Mono.empty();
        }
        
        try {
            String jsonMessage = objectMapper.writeValueAsString(message);
            WebSocketMessage wsMessage = clientA.getSession().textMessage(jsonMessage);
            
            if (wsMessage == null) {
                logger.error("Failed to create WebSocket message for Client A {} (response sessionId: {})", 
                    clientA.getSession().getId(), message.getSessionId());
                return Mono.empty();
            }
            
            logger.info("Routing response from Client B {} to Client A {} (sessionId: {}, statusCode: {})", 
                clientBSessionId, clientA.getSession().getId(), 
                message.getSessionId(), message.getStatusCode());
            
            return clientA.getSession().send(Mono.just(wsMessage))
                .doOnSuccess(v -> logger.debug("Successfully routed response to Client A {} (sessionId: {})", 
                    clientA.getSession().getId(), message.getSessionId()))
                .doOnError(e -> logger.error("Error sending response to Client A {} (sessionId: {})", 
                    clientA.getSession().getId(), message.getSessionId(), e))
                .doOnCancel(() -> logger.warn("Response routing cancelled for Client A {} (sessionId: {})", 
                    clientA.getSession().getId(), message.getSessionId()));
        } catch (Exception e) {
            logger.error("Error routing response to Client A (sessionId: {})", 
                message.getSessionId(), e);
            return Mono.empty();
        }
    }
    
    /**
     * 에러 응답 전송
     */
    private Mono<Void> sendErrorResponse(String clientASessionId, String requestSessionId, String errorMessage) {
        SessionInfo clientA = sessionService.getClientA(clientASessionId);
        if (clientA == null || !clientA.isActive()) {
            logger.warn("Cannot send error response: Client A {} is not active (request sessionId: {})", 
                clientASessionId, requestSessionId);
            return Mono.empty();
        }
        
        try {
            RelayMessage errorResponse = new RelayMessage();
            errorResponse.setType(RelayMessage.MessageType.RESPONSE);
            errorResponse.setSessionId(requestSessionId); // 원래 요청의 sessionId 포함
            errorResponse.setStatusCode(500);
            errorResponse.setError(errorMessage);
            
            String jsonMessage = objectMapper.writeValueAsString(errorResponse);
            WebSocketMessage wsMessage = clientA.getSession().textMessage(jsonMessage);
            
            if (wsMessage == null) {
                logger.error("Failed to create error response message for Client A {} (request sessionId: {})", 
                    clientASessionId, requestSessionId);
                return Mono.empty();
            }
            
            logger.info("Sending error response to Client A {} (sessionId: {}, error: {})", 
                clientASessionId, requestSessionId, errorMessage);
            
            return clientA.getSession().send(Mono.just(wsMessage))
                .doOnSuccess(v -> logger.debug("Error response sent to Client A {} (sessionId: {})", 
                    clientASessionId, requestSessionId))
                .doOnError(e -> logger.error("Error sending error response to Client A {} (sessionId: {})", 
                    clientASessionId, requestSessionId, e));
        } catch (Exception e) {
            logger.error("Error creating error response (sessionId: {})", requestSessionId, e);
            return Mono.empty();
        }
    }
    
    /**
     * JSON 메시지 파싱
     */
    public RelayMessage parseMessage(String json) {
        // 빈 메시지 체크
        if (json == null || json.trim().isEmpty()) {
            logger.warn("Empty message received, cannot parse");
            return null;
        }
        
        try {
            return objectMapper.readValue(json, RelayMessage.class);
        } catch (Exception e) {
            logger.error("Error parsing message (length: {}): {}", json.length(), 
                json.length() > 100 ? json.substring(0, 100) + "..." : json, e);
            return null;
        }
    }
}

