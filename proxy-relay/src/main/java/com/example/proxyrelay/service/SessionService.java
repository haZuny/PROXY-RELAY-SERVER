package com.example.proxyrelay.service;

import com.example.proxyrelay.dto.ClientType;
import com.example.proxyrelay.dto.SessionInfo;
import org.springframework.stereotype.Service;
import org.springframework.web.reactive.socket.WebSocketSession;
import reactor.core.publisher.Mono;

import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.ConcurrentMap;

/**
 * 세션 관리 서비스
 * Client A (외부 프록시)와 Client B (내부 에이전트)의 매핑 관리
 */
@Service
public class SessionService {
    
    // Client A 세션 저장 (외부 프록시)
    private final ConcurrentMap<String, SessionInfo> clientASessions = new ConcurrentHashMap<>();
    
    // Client B 세션 저장 (내부 에이전트)
    private final ConcurrentMap<String, SessionInfo> clientBSessions = new ConcurrentHashMap<>();
    
    // 세션 ID로 Client A와 Client B 매핑
    private final ConcurrentMap<String, String> sessionMapping = new ConcurrentHashMap<>();
    
    /**
     * 세션 등록
     */
    public void registerSession(WebSocketSession session, ClientType clientType, String accessToken) {
        String sessionId = session.getId();
        SessionInfo sessionInfo = new SessionInfo(session, clientType, accessToken);
        
        if (clientType == ClientType.CLIENT_A) {
            clientASessions.put(sessionId, sessionInfo);
        } else {
            clientBSessions.put(sessionId, sessionInfo);
        }
    }
    
    /**
     * 세션 제거
     */
    public void removeSession(String sessionId) {
        SessionInfo sessionInfo = clientASessions.remove(sessionId);
        if (sessionInfo == null) {
            sessionInfo = clientBSessions.remove(sessionId);
        }
        
        // 매핑 제거
        sessionMapping.entrySet().removeIf(entry -> 
            entry.getKey().equals(sessionId) || entry.getValue().equals(sessionId)
        );
    }
    
    /**
     * Client A 세션 조회
     */
    public SessionInfo getClientA(String sessionId) {
        return clientASessions.get(sessionId);
    }
    
    /**
     * Client B 세션 조회
     */
    public SessionInfo getClientB(String sessionId) {
        return clientBSessions.get(sessionId);
    }
    
    /**
     * 세션 매핑 생성 (Client A ↔ Client B)
     */
    public void mapSessions(String clientASessionId, String clientBSessionId) {
        sessionMapping.put(clientASessionId, clientBSessionId);
    }
    
    /**
     * Client A의 세션 ID로 매핑된 Client B 세션 조회
     */
    public SessionInfo getMappedClientB(String clientASessionId) {
        String clientBSessionId = sessionMapping.get(clientASessionId);
        if (clientBSessionId != null) {
            return clientBSessions.get(clientBSessionId);
        }
        return null;
    }
    
    /**
     * Client B의 세션 ID로 매핑된 Client A 세션 조회
     */
    public SessionInfo getMappedClientA(String clientBSessionId) {
        return sessionMapping.entrySet().stream()
            .filter(entry -> entry.getValue().equals(clientBSessionId))
            .map(entry -> clientASessions.get(entry.getKey()))
            .findFirst()
            .orElse(null);
    }
    
    /**
     * 사용 가능한 Client B 세션 찾기 (매핑되지 않은 첫 번째 활성 세션 반환)
     */
    public SessionInfo findAvailableClientB() {
        return clientBSessions.values().stream()
            .filter(SessionInfo::isActive)
            .filter(session -> {
                // 이미 매핑된 Client B인지 확인
                String sessionId = session.getSession().getId();
                return sessionMapping.values().stream()
                    .noneMatch(mappedBId -> mappedBId.equals(sessionId));
            })
            .findFirst()
            .orElse(null);
    }
    
    /**
     * 사용 가능한 Client A 세션 찾기 (매핑되지 않은 첫 번째 활성 세션 반환)
     */
    public SessionInfo findAvailableClientA() {
        return clientASessions.values().stream()
            .filter(SessionInfo::isActive)
            .filter(session -> !sessionMapping.containsKey(session.getSession().getId()))
            .findFirst()
            .orElse(null);
    }
    
    /**
     * 활성 세션 수 조회
     */
    public int getActiveClientACount() {
        return (int) clientASessions.values().stream()
            .filter(SessionInfo::isActive)
            .count();
    }
    
    public int getActiveClientBCount() {
        return (int) clientBSessions.values().stream()
            .filter(SessionInfo::isActive)
            .count();
    }
}

