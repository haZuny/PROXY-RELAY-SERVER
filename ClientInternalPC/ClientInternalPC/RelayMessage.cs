using System.Collections.Generic;

namespace ClientInternalPC
{
    /// <summary>
    /// Relay Server와 주고받는 메시지 포맷
    /// </summary>
    public class RelayMessage
    {
        public string Type { get; set; }  // REQUEST, RESPONSE, PING, PONG
        public string SessionId { get; set; }
        public string Method { get; set; }  // GET, POST, PUT, DELETE 등
        public string Url { get; set; }
        public Dictionary<string, string> Headers { get; set; }
        public string Body { get; set; }
        public int? StatusCode { get; set; }
        public string Error { get; set; }
    }
}

