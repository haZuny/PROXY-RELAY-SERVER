package com.example.proxyrelay.dto;

import org.springframework.web.reactive.socket.WebSocketSession;

/**
 * 세션 정보를 담는 클래스
 */
public class SessionInfo {
    private WebSocketSession session;
    private ClientType clientType;
    private String accessToken;
    private long connectedAt;
    
    public SessionInfo(WebSocketSession session, ClientType clientType, String accessToken) {
        this.session = session;
        this.clientType = clientType;
        this.accessToken = accessToken;
        this.connectedAt = System.currentTimeMillis();
    }
    
    public WebSocketSession getSession() {
        return session;
    }
    
    public ClientType getClientType() {
        return clientType;
    }
    
    public String getAccessToken() {
        return accessToken;
    }
    
    public long getConnectedAt() {
        return connectedAt;
    }
    
    public boolean isActive() {
        return session != null && session.isOpen();
    }
}

