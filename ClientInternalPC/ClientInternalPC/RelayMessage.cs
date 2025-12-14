using System.Collections.Generic;
using Newtonsoft.Json;

namespace ClientInternalPC
{
    /// <summary>
    /// Relay Server와 주고받는 메시지 포맷
    /// 서버는 Java/Jackson을 사용하므로 camelCase 필드명을 사용합니다.
    /// </summary>
    public class RelayMessage
    {
        [JsonProperty("type")]
        public string Type { get; set; }  // REQUEST, RESPONSE, PING, PONG

        [JsonProperty("sessionId")]
        public string SessionId { get; set; }

        [JsonProperty("method")]
        public string Method { get; set; }  // GET, POST, PUT, DELETE 등

        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("headers")]
        public Dictionary<string, string> Headers { get; set; }

        [JsonProperty("body")]
        public string Body { get; set; }

        [JsonProperty("statusCode")]
        public int? StatusCode { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }
    }
}

