using System;
using System.IO;
using System.Text;

namespace FroxyServerClient
{
    /// <summary>
    /// PAC 파일 생성 및 관리 클래스
    /// </summary>
    public class PacFileGenerator
    {
        private const string PacFileName = "froxy_proxy.pac";
        private string _pacFilePath;

        public PacFileGenerator()
        {
            // 임시 폴더에 PAC 파일 생성
            string tempPath = Path.GetTempPath();
            _pacFilePath = Path.Combine(tempPath, PacFileName);
        }

        /// <summary>
        /// PAC 파일 생성
        /// </summary>
        /// <param name="proxyHost">프록시 서버 IP 또는 호스트명</param>
        /// <param name="proxyPort">프록시 서버 포트</param>
        /// <param name="domainFilters">프록시로 보낼 도메인 필터 목록 (쉼표로 구분)</param>
        /// <returns>생성된 PAC 파일 경로</returns>
        public string GeneratePacFile(string proxyHost, int proxyPort, string domainFilters = null)
        {
            if (string.IsNullOrWhiteSpace(proxyHost))
                throw new ArgumentException("프록시 호스트가 필요합니다.", nameof(proxyHost));

            if (proxyPort <= 0 || proxyPort > 65535)
                throw new ArgumentException("유효한 포트 번호가 필요합니다.", nameof(proxyPort));

            string pacContent = GeneratePacScript(proxyHost, proxyPort, domainFilters);
            
            try
            {
                File.WriteAllText(_pacFilePath, pacContent, Encoding.UTF8);
                return _pacFilePath;
            }
            catch (Exception ex)
            {
                throw new Exception($"PAC 파일 생성 실패: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// PAC 스크립트 내용 생성
        /// </summary>
        private string GeneratePacScript(string proxyHost, int proxyPort, string domainFilters)
        {
            StringBuilder conditions = new StringBuilder();
            
            if (!string.IsNullOrWhiteSpace(domainFilters))
            {
                // 도메인 필터 파싱
                string[] filters = domainFilters.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                
                foreach (string filter in filters)
                {
                    string trimmed = filter.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed))
                        continue;

                    // IP 대역 형식 (예: 10.0.0.0/8, 172.16.0.0/12)
                    if (trimmed.Contains("/"))
                    {
                        string[] parts = trimmed.Split('/');
                        if (parts.Length == 2)
                        {
                            string ip = parts[0].Trim();
                            int prefix = 0;
                            if (int.TryParse(parts[1].Trim(), out prefix))
                            {
                                string subnet = ConvertPrefixToSubnet(prefix);
                                conditions.AppendLine($"        isInNet(host, \"{ip}\", \"{subnet}\") ||");
                            }
                        }
                    }
                    // 와일드카드 도메인 (예: *.hospital.local)
                    else if (trimmed.StartsWith("*."))
                    {
                        string domain = trimmed.Substring(2);
                        conditions.AppendLine($"        (dnsDomainIs(host, \".{domain}\") || shExpMatch(host, \"{trimmed}\")) ||");
                    }
                    // 일반 도메인 (예: hospital.local)
                    else if (trimmed.Contains("."))
                    {
                        conditions.AppendLine($"        (dnsDomainIs(host, \".{trimmed}\") || shExpMatch(host, \"*.{trimmed}\")) ||");
                    }
                    // IP 주소 (예: 10.0.0.0)
                    else if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^\d+\.\d+\.\d+\.\d+$"))
                    {
                        // 단일 IP는 /32로 처리
                        conditions.AppendLine($"        isInNet(host, \"{trimmed}\", \"255.255.255.255\") ||");
                    }
                }
            }
            
            // 기본값이 없으면 기본 필터 사용
            if (conditions.Length == 0)
            {
                conditions.AppendLine(@"        dnsDomainIs(host, "".hospital.local"") ||
        shExpMatch(host, ""*.hospital.local"") ||
        isInNet(host, ""10.0.0.0"", ""255.0.0.0"") ||
        isInNet(host, ""172.16.0.0"", ""255.240.0.0"") ||
        isInNet(host, ""192.168.0.0"", ""255.255.0.0"") ||");
            }

            // 마지막 || 제거
            if (conditions.Length > 0)
            {
                string conditionStr = conditions.ToString().TrimEnd();
                if (conditionStr.EndsWith("||"))
                {
                    conditionStr = conditionStr.Substring(0, conditionStr.Length - 2).TrimEnd();
                }
                conditions.Clear();
                conditions.Append(conditionStr);
            }

            return $@"function FindProxyForURL(url, host) {{
    // 내부망 도메인 체크
    if ({conditions.ToString()}) {{
        return ""PROXY {proxyHost}:{proxyPort}"";
    }}
    // 나머지는 직접 연결
    return ""DIRECT"";
}}";
        }

        /// <summary>
        /// CIDR prefix를 서브넷 마스크로 변환
        /// </summary>
        private string ConvertPrefixToSubnet(int prefix)
        {
            if (prefix <= 0 || prefix > 32)
                return "255.255.255.255";

            uint mask = 0xFFFFFFFF << (32 - prefix);
            return $"{(mask >> 24) & 0xFF}.{(mask >> 16) & 0xFF}.{(mask >> 8) & 0xFF}.{mask & 0xFF}";
        }

        /// <summary>
        /// PAC 파일 경로 반환
        /// </summary>
        public string GetPacFilePath()
        {
            return _pacFilePath;
        }

        /// <summary>
        /// PAC 파일 삭제
        /// </summary>
        public void DeletePacFile()
        {
            try
            {
                if (File.Exists(_pacFilePath))
                {
                    File.Delete(_pacFilePath);
                }
            }
            catch (Exception ex)
            {
                // 삭제 실패는 무시 (파일이 사용 중일 수 있음)
                System.Diagnostics.Debug.WriteLine($"PAC 파일 삭제 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// PAC 파일이 존재하는지 확인
        /// </summary>
        public bool PacFileExists()
        {
            return File.Exists(_pacFilePath);
        }
    }
}

