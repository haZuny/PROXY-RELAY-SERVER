package com.example.proxyrelay.service;

import org.springframework.beans.factory.annotation.Value;
import org.springframework.stereotype.Service;

/**
 * 인증 서비스
 * Access Token 검증
 */
@Service
public class AuthService {
    
    @Value("${relay.access-token:default-token-change-in-production}")
    private String validAccessToken;
    
    /**
     * Access Token 검증
     */
    public boolean validateToken(String token) {
        if (token == null || token.isEmpty()) {
            return false;
        }
        return validAccessToken.equals(token);
    }
    
    /**
     * Query Parameter나 Header에서 토큰 추출
     */
    public String extractToken(String query) {
        if (query == null || query.isEmpty()) {
            return null;
        }
        
        // token=xxx 형식에서 추출
        String[] params = query.split("&");
        for (String param : params) {
            if (param.startsWith("token=")) {
                return param.substring(6);
            }
        }
        return null;
    }
}

