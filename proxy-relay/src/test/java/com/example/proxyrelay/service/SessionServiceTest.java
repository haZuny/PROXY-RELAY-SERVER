package com.example.proxyrelay.service;

import com.example.proxyrelay.dto.ClientType;
import com.example.proxyrelay.dto.SessionInfo;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.springframework.web.reactive.socket.WebSocketSession;

import static org.junit.jupiter.api.Assertions.*;
import static org.mockito.Mockito.*;

/**
 * SessionService 테스트
 * 세션 등록, 제거, 매핑 관리 기능을 테스트합니다.
 */
class SessionServiceTest {
    
    private SessionService sessionService;
    private WebSocketSession mockSessionA;
    private WebSocketSession mockSessionB;
    
    @BeforeEach
    void setUp() {
        sessionService = new SessionService();
        mockSessionA = mock(WebSocketSession.class);
        mockSessionB = mock(WebSocketSession.class);
        
        when(mockSessionA.getId()).thenReturn("session-a-1");
        when(mockSessionB.getId()).thenReturn("session-b-1");
        when(mockSessionA.isOpen()).thenReturn(true);
        when(mockSessionB.isOpen()).thenReturn(true);
    }
    
    /**
     * 검증: Client A 세션이 올바르게 등록되고 조회되어야 함
     * 목적: 외부 프록시(Client A) 세션 등록 및 활성 세션 수 카운트 확인
     */
    @Test
    void registerSession_ClientA_RegistersSuccessfully() {
        sessionService.registerSession(mockSessionA, ClientType.CLIENT_A, "token");
        
        SessionInfo sessionInfo = sessionService.getClientA("session-a-1");
        assertNotNull(sessionInfo);
        assertEquals(ClientType.CLIENT_A, sessionInfo.getClientType());
        assertEquals(1, sessionService.getActiveClientACount());
    }
    
    /**
     * 검증: Client B 세션이 올바르게 등록되고 조회되어야 함
     * 목적: 내부 에이전트(Client B) 세션 등록 및 활성 세션 수 카운트 확인
     */
    @Test
    void registerSession_ClientB_RegistersSuccessfully() {
        sessionService.registerSession(mockSessionB, ClientType.CLIENT_B, "token");
        
        SessionInfo sessionInfo = sessionService.getClientB("session-b-1");
        assertNotNull(sessionInfo);
        assertEquals(ClientType.CLIENT_B, sessionInfo.getClientType());
        assertEquals(1, sessionService.getActiveClientBCount());
    }
    
    /**
     * 검증: 세션 제거 시 해당 세션만 제거되고 다른 세션은 유지되어야 함
     * 목적: 세션 제거 시 Client A/B 맵에서 올바르게 제거되는지 확인
     */
    @Test
    void removeSession_RemovesFromBothMaps() {
        sessionService.registerSession(mockSessionA, ClientType.CLIENT_A, "token");
        sessionService.registerSession(mockSessionB, ClientType.CLIENT_B, "token");
        
        sessionService.removeSession("session-a-1");
        
        assertNull(sessionService.getClientA("session-a-1"));
        assertNotNull(sessionService.getClientB("session-b-1"));
        assertEquals(0, sessionService.getActiveClientACount());
    }
    
    /**
     * 검증: Client A와 Client B 간의 양방향 매핑이 올바르게 생성되어야 함
     * 목적: 세션 매핑 생성 후 양방향 조회가 가능한지 확인
     */
    @Test
    void mapSessions_CreatesMapping() {
        sessionService.registerSession(mockSessionA, ClientType.CLIENT_A, "token");
        sessionService.registerSession(mockSessionB, ClientType.CLIENT_B, "token");
        
        sessionService.mapSessions("session-a-1", "session-b-1");
        
        SessionInfo mappedB = sessionService.getMappedClientB("session-a-1");
        assertNotNull(mappedB);
        assertEquals("session-b-1", mappedB.getSession().getId());
        
        SessionInfo mappedA = sessionService.getMappedClientA("session-b-1");
        assertNotNull(mappedA);
        assertEquals("session-a-1", mappedA.getSession().getId());
    }
    
    /**
     * 검증: 매핑이 없는 경우 null을 반환해야 함
     * 목적: 매핑되지 않은 세션에 대한 안전한 처리 확인
     */
    @Test
    void getMappedClientB_NoMapping_ReturnsNull() {
        sessionService.registerSession(mockSessionA, ClientType.CLIENT_A, "token");
        
        SessionInfo mappedB = sessionService.getMappedClientB("session-a-1");
        assertNull(mappedB);
    }
    
    /**
     * 검증: 매핑되지 않은 Client A 세션을 찾을 수 있어야 함
     * 목적: 새로운 Client B가 연결될 때 사용 가능한 Client A를 찾는 로직 확인
     */
    @Test
    void findAvailableClientA_ReturnsUnmappedSession() {
        WebSocketSession sessionA1 = mock(WebSocketSession.class);
        WebSocketSession sessionA2 = mock(WebSocketSession.class);
        when(sessionA1.getId()).thenReturn("session-a-1");
        when(sessionA2.getId()).thenReturn("session-a-2");
        when(sessionA1.isOpen()).thenReturn(true);
        when(sessionA2.isOpen()).thenReturn(true);
        
        sessionService.registerSession(sessionA1, ClientType.CLIENT_A, "token");
        sessionService.registerSession(sessionA2, ClientType.CLIENT_A, "token");
        sessionService.registerSession(mockSessionB, ClientType.CLIENT_B, "token");
        
        // session-a-1을 매핑
        sessionService.mapSessions("session-a-1", "session-b-1");
        
        // 매핑되지 않은 session-a-2가 반환되어야 함
        SessionInfo available = sessionService.findAvailableClientA();
        assertNotNull(available);
        assertEquals("session-a-2", available.getSession().getId());
    }
    
    /**
     * 검증: 활성 Client A 세션 수가 정확하게 카운트되어야 함
     * 목적: 통계 정보 제공을 위한 세션 수 집계 기능 확인
     */
    @Test
    void getActiveClientACount_ReturnsCorrectCount() {
        WebSocketSession session1 = mock(WebSocketSession.class);
        WebSocketSession session2 = mock(WebSocketSession.class);
        when(session1.getId()).thenReturn("session-1");
        when(session2.getId()).thenReturn("session-2");
        when(session1.isOpen()).thenReturn(true);
        when(session2.isOpen()).thenReturn(true);
        
        sessionService.registerSession(session1, ClientType.CLIENT_A, "token");
        sessionService.registerSession(session2, ClientType.CLIENT_A, "token");
        
        assertEquals(2, sessionService.getActiveClientACount());
    }
}

