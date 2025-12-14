package com.example.proxyrelay.dto;

import com.fasterxml.jackson.annotation.JsonProperty;

/**
 * Relay Server를 통과하는 메시지 포맷
 */
public class RelayMessage {
    
    @JsonProperty("type")
    private MessageType type;
    
    @JsonProperty("sessionId")
    private String sessionId;
    
    @JsonProperty("method")
    private String method;
    
    @JsonProperty("url")
    private String url;
    
    @JsonProperty("headers")
    private java.util.Map<String, String> headers;
    
    @JsonProperty("body")
    private String body;
    
    @JsonProperty("statusCode")
    private Integer statusCode;
    
    @JsonProperty("error")
    private String error;
    
    public enum MessageType {
        REQUEST,    // 요청 메시지
        RESPONSE,   // 응답 메시지
        PING,       // 연결 유지
        PONG        // 연결 유지 응답
    }
    
    // Getters and Setters
    public MessageType getType() {
        return type;
    }
    
    public void setType(MessageType type) {
        this.type = type;
    }
    
    public String getSessionId() {
        return sessionId;
    }
    
    public void setSessionId(String sessionId) {
        this.sessionId = sessionId;
    }
    
    public String getMethod() {
        return method;
    }
    
    public void setMethod(String method) {
        this.method = method;
    }
    
    public String getUrl() {
        return url;
    }
    
    public void setUrl(String url) {
        this.url = url;
    }
    
    public java.util.Map<String, String> getHeaders() {
        return headers;
    }
    
    public void setHeaders(java.util.Map<String, String> headers) {
        this.headers = headers;
    }
    
    public String getBody() {
        return body;
    }
    
    public void setBody(String body) {
        this.body = body;
    }
    
    public Integer getStatusCode() {
        return statusCode;
    }
    
    public void setStatusCode(Integer statusCode) {
        this.statusCode = statusCode;
    }
    
    public String getError() {
        return error;
    }
    
    public void setError(String error) {
        this.error = error;
    }
}

