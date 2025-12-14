package com.example.proxyrelay.config;

import com.example.proxyrelay.handler.RelayWebSocketHandler;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.web.reactive.HandlerMapping;
import org.springframework.web.reactive.handler.SimpleUrlHandlerMapping;
import org.springframework.web.reactive.socket.WebSocketHandler;
import org.springframework.web.reactive.socket.server.support.WebSocketHandlerAdapter;

import java.util.HashMap;
import java.util.Map;

/**
 * WebSocket 설정
 * 순수 WebSocket 사용 (STOMP/SockJS 제거)
 */
@Configuration
public class WebSocketConfig {
    
    private final RelayWebSocketHandler relayWebSocketHandler;
    
    public WebSocketConfig(RelayWebSocketHandler relayWebSocketHandler) {
        this.relayWebSocketHandler = relayWebSocketHandler;
    }
    
    @Bean
    public HandlerMapping webSocketHandlerMapping() {
        Map<String, WebSocketHandler> map = new HashMap<>();
        map.put("/relay", relayWebSocketHandler);
        
        SimpleUrlHandlerMapping mapping = new SimpleUrlHandlerMapping();
        mapping.setUrlMap(map);
        mapping.setOrder(1);
        return mapping;
    }
    
    @Bean
    public WebSocketHandlerAdapter handlerAdapter() {
        return new WebSocketHandlerAdapter();
    }
}

