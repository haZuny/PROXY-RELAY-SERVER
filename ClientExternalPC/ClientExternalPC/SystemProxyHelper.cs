using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ClientExternalPC
{
    /// <summary>
    /// Windows 시스템 프록시 설정을 관리하는 헬퍼 클래스
    /// </summary>
    public static class SystemProxyHelper
    {
        private const string REGISTRY_KEY = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

        /// <summary>
        /// 시스템 프록시 설정
        /// </summary>
        public static bool SetSystemProxy(string proxyServer, bool enable = true)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(REGISTRY_KEY, true))
                {
                    if (key == null)
                        return false;

                    if (enable)
                    {
                        key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
                        key.SetValue("ProxyServer", proxyServer, RegistryValueKind.String);
                    }
                    else
                    {
                        key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                    }

                    // 변경사항을 즉시 적용하기 위해 WinINet API 호출
                    InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0);
                    InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0);

                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"시스템 프록시 설정 실패: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 시스템 프록시 비활성화
        /// </summary>
        public static bool DisableSystemProxy()
        {
            return SetSystemProxy("", false);
        }

        /// <summary>
        /// 현재 시스템 프록시 설정 가져오기
        /// </summary>
        public static string GetSystemProxy()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(REGISTRY_KEY, false))
                {
                    if (key == null)
                        return null;

                    var enabled = key.GetValue("ProxyEnable");
                    if (enabled != null && Convert.ToInt32(enabled) == 1)
                    {
                        return key.GetValue("ProxyServer")?.ToString();
                    }
                }
            }
            catch { }

            return null;
        }

        [DllImport("wininet.dll")]
        private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);
    }
}

