using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace FroxyServerClient
{
    /// <summary>
    /// Windows 프록시 설정 관리 클래스
    /// </summary>
    public class ProxyManager
    {
        private const string WinHttpProxyKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Internet Settings";

        /// <summary>
        /// PAC 파일을 사용하여 프록시 설정 적용
        /// </summary>
        /// <param name="pacFilePath">PAC 파일 경로 (file:// 형식)</param>
        /// <returns>성공 여부</returns>
        public bool SetProxyWithPac(string pacFilePath)
        {
            if (string.IsNullOrWhiteSpace(pacFilePath))
                throw new ArgumentException("PAC 파일 경로가 필요합니다.", nameof(pacFilePath));

            if (!File.Exists(pacFilePath))
                throw new FileNotFoundException("PAC 파일을 찾을 수 없습니다.", pacFilePath);

            try
            {
                // file:// URL 형식으로 변환
                string pacUrl = new Uri(pacFilePath).AbsoluteUri;

                // 주로 레지스트리 방식 사용 (관리자 권한 불필요)
                // HKEY_CURRENT_USER는 현재 사용자 권한으로 수정 가능
                SetProxyViaRegistry(pacUrl);

                // netsh winhttp는 선택적으로 시도 (시스템 전체 설정용)
                // 실패해도 레지스트리 설정은 이미 완료되었으므로 문제없음
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments = $"winhttp set proxy proxy-server=\"{pacUrl}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    Process process = Process.Start(psi);
                    if (process != null)
                    {
                        process.WaitForExit(3000); // 3초 타임아웃
                    }
                }
                catch
                {
                    // netsh 실패는 무시 (레지스트리 설정이 더 중요)
                }

                return true;
            }
            catch (Exception ex)
            {
                throw new Exception($"프록시 설정 실패: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 레지스트리를 통한 프록시 설정
        /// </summary>
        private void SetProxyViaRegistry(string pacUrl)
        {
            try
            {
                Registry.SetValue(WinHttpProxyKey, "AutoConfigURL", pacUrl);
                Registry.SetValue(WinHttpProxyKey, "ProxyEnable", 0); // PAC 사용 시 ProxyEnable은 0
            }
            catch (Exception ex)
            {
                throw new Exception($"레지스트리 설정 실패: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 프록시 설정 해제
        /// </summary>
        /// <returns>성공 여부</returns>
        public bool DisableProxy()
        {
            try
            {
                // 레지스트리에서 제거 (주요 방법)
                Registry.SetValue(WinHttpProxyKey, "AutoConfigURL", "");
                Registry.SetValue(WinHttpProxyKey, "ProxyEnable", 0);

                // netsh winhttp reset은 선택적으로 시도
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments = "winhttp reset proxy",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    Process process = Process.Start(psi);
                    if (process != null)
                    {
                        process.WaitForExit(3000); // 3초 타임아웃
                    }
                }
                catch
                {
                    // netsh 실패는 무시
                }

                return true;
            }
            catch (Exception ex)
            {
                throw new Exception($"프록시 해제 실패: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 현재 프록시 설정 상태 확인
        /// </summary>
        /// <returns>프록시 활성화 여부</returns>
        public bool IsProxyEnabled()
        {
            try
            {
                object autoConfigUrl = Registry.GetValue(WinHttpProxyKey, "AutoConfigURL", null);
                return autoConfigUrl != null && !string.IsNullOrWhiteSpace(autoConfigUrl.ToString());
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 현재 PAC 파일 경로 가져오기
        /// </summary>
        /// <returns>PAC 파일 경로 (없으면 null)</returns>
        public string GetCurrentPacPath()
        {
            try
            {
                object autoConfigUrl = Registry.GetValue(WinHttpProxyKey, "AutoConfigURL", null);
                if (autoConfigUrl != null)
                {
                    string url = autoConfigUrl.ToString();
                    if (url.StartsWith("file:///"))
                    {
                        // file:///C:/path/to/file.pac 형식을 일반 경로로 변환
                        return new Uri(url).LocalPath;
                    }
                    return url;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}

