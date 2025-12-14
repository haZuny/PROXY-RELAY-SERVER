package com.example.proxyrelay.service;

import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.springframework.test.util.ReflectionTestUtils;

import static org.junit.jupiter.api.Assertions.*;

/**
 * AuthService 테스트
 * 인증 토큰 검증 및 추출 기능을 테스트합니다.
 */
class AuthServiceTest {
    
    private AuthService authService;
    private static final String VALID_TOKEN = "test-token-123";
    
    @BeforeEach
    void setUp() {
        authService = new AuthService();
        ReflectionTestUtils.setField(authService, "validAccessToken", VALID_TOKEN);
    }
    
    /**
     * 검증: 유효한 토큰이 주어지면 true를 반환해야 함
     * 목적: 정상적인 인증 토큰이 올바르게 검증되는지 확인
     */
    @Test
    void validateToken_ValidToken_ReturnsTrue() {
        assertTrue(authService.validateToken(VALID_TOKEN));
    }
    
    /**
     * 검증: 잘못된 토큰이 주어지면 false를 반환해야 함
     * 목적: 무효한 토큰이 거부되는지 확인
     */
    @Test
    void validateToken_InvalidToken_ReturnsFalse() {
        assertFalse(authService.validateToken("wrong-token"));
    }
    
    /**
     * 검증: null 토큰이 주어지면 false를 반환해야 함
     * 목적: null 값에 대한 안전한 처리 확인
     */
    @Test
    void validateToken_NullToken_ReturnsFalse() {
        assertFalse(authService.validateToken(null));
    }
    
    /**
     * 검증: 빈 문자열 토큰이 주어지면 false를 반환해야 함
     * 목적: 빈 값에 대한 안전한 처리 확인
     */
    @Test
    void validateToken_EmptyToken_ReturnsFalse() {
        assertFalse(authService.validateToken(""));
    }
    
    /**
     * 검증: Query String에서 토큰을 올바르게 추출할 수 있어야 함
     * 목적: "type=A&token=xxx&other=value" 형식에서 token 값 추출 확인
     */
    @Test
    void extractToken_ValidQuery_ReturnsToken() {
        String query = "type=A&token=test-token-123&other=value";
        String token = authService.extractToken(query);
        assertEquals("test-token-123", token);
    }
    
    /**
     * 검증: 토큰이 Query String의 첫 번째 파라미터여도 추출 가능해야 함
     * 목적: 파라미터 순서와 관계없이 토큰 추출이 가능한지 확인
     */
    @Test
    void extractToken_TokenFirst_ReturnsToken() {
        String query = "token=test-token-123&type=A";
        String token = authService.extractToken(query);
        assertEquals("test-token-123", token);
    }
    
    /**
     * 검증: Query String에 토큰이 없으면 null을 반환해야 함
     * 목적: 토큰이 없는 경우의 처리 확인
     */
    @Test
    void extractToken_NoToken_ReturnsNull() {
        String query = "type=A&other=value";
        String token = authService.extractToken(query);
        assertNull(token);
    }
    
    /**
     * 검증: null Query String이 주어지면 null을 반환해야 함
     * 목적: null 입력에 대한 안전한 처리 확인
     */
    @Test
    void extractToken_NullQuery_ReturnsNull() {
        String token = authService.extractToken(null);
        assertNull(token);
    }
    
    /**
     * 검증: 빈 Query String이 주어지면 null을 반환해야 함
     * 목적: 빈 입력에 대한 안전한 처리 확인
     */
    @Test
    void extractToken_EmptyQuery_ReturnsNull() {
        String token = authService.extractToken("");
        assertNull(token);
    }
}

