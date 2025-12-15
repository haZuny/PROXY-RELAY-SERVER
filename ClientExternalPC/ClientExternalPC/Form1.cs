using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ClientExternalPC
{
    public partial class Form1 : Form
    {
        private ProxyClientA _proxyClient;
        private bool _isRunning = false;
        private string _relayServerUrl = "http://localhost:8080";  // Relay Server 기본 주소
        private int _proxyPort = 8888;
        private string _domainFilter = ""; // 빈 문자열이면 모든 도메인 허용, 아니면 콤마로 구분된 도메인 목록
        private string _accessToken = "default-token-change-in-production";  // Relay Server에 설정된 토큰
        private LogForm _logForm;

        public Form1()
        {
            InitializeComponent();
            InitializeTrayIcon();
            this.WindowState = FormWindowState.Normal;
            this.ShowInTaskbar = true;
            
            // 로그 폼 초기화
            _logForm = new LogForm();
            
            // 이벤트 핸들러 연결
            this.btnToggle.Click += BtnToggle_Click;
            this.btnSettings.Click += BtnSettings_Click;
            
            UpdateStatusLabel();
            UpdateToggleButton();
        }

        private void InitializeTrayIcon()
        {
            notifyIcon1.Icon = SystemIcons.Application;
            notifyIcon1.Text = "Proxy Client A";
            notifyIcon1.Visible = true;
            notifyIcon1.ContextMenuStrip = contextMenuStrip1;
            notifyIcon1.DoubleClick += NotifyIcon1_DoubleClick;
        }

        private async void NotifyIcon1_DoubleClick(object sender, EventArgs e)
        {
            ShowSettingsForm();
        }

        private async void BtnToggle_Click(object sender, EventArgs e)
        {
            if (!_isRunning)
            {
                await StartProxy();
            }
            else
            {
                await StopProxy();
            }
        }

        private void BtnSettings_Click(object sender, EventArgs e)
        {
            ShowSettingsForm();
        }

        private async void 시작ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!_isRunning)
            {
                await StartProxy();
            }
        }

        private async void 중지ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (_isRunning)
            {
                await StopProxy();
            }
        }

        private void 설정ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ShowSettingsForm();
        }

        private void 로깅ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (_logForm != null && !_logForm.IsDisposed)
            {
                if (_logForm.Visible)
                {
                    _logForm.Hide();
                }
                else
                {
                    _logForm.Show();
                    _logForm.BringToFront();
                }
            }
        }

        private void 종료ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // 프록시 중지 및 시스템 프록시 비활성화
            if (_isRunning)
            {
                StopProxy().Wait(3000);
            }
            else
            {
                // 실행 중이 아니어도 시스템 프록시는 비활성화 (안전장치)
                SystemProxyHelper.DisableSystemProxy();
            }
            
            // 로그 폼 정리
            if (_logForm != null && !_logForm.IsDisposed)
            {
                _logForm.Close();
            }
            
            Application.Exit();
        }

        private async Task StartProxy()
        {
            try
            {
                _proxyClient = new ProxyClientA(_relayServerUrl, _proxyPort, _domainFilter, _accessToken);
                _proxyClient.LogMessage += ProxyClient_LogMessage;
                _proxyClient.ConnectionStatusChanged += ProxyClient_ConnectionStatusChanged;

                await _proxyClient.StartAsync();
                _isRunning = true;

                // Windows 시스템 프록시 자동 설정
                var proxyServer = $"127.0.0.1:{_proxyPort}";
                var currentProxy = SystemProxyHelper.GetSystemProxy();
                OnLogMessage($"[시스템 프록시 확인] 현재 시스템 프록시: {currentProxy ?? "없음"}");
                
                if (SystemProxyHelper.SetSystemProxy(proxyServer, true))
                {
                    OnLogMessage($"[시스템 프록시] Windows 시스템 프록시가 자동으로 설정되었습니다: {proxyServer}");
                    
                    // 설정 확인
                    var verifyProxy = SystemProxyHelper.GetSystemProxy();
                    if (verifyProxy == proxyServer)
                    {
                        OnLogMessage($"[시스템 프록시 확인] 프록시 설정 확인됨: {verifyProxy}");
                    }
                    else
                    {
                        OnLogMessage($"[시스템 프록시 경고] 프록시 설정 확인 실패. 예상: {proxyServer}, 실제: {verifyProxy ?? "없음"}");
                    }
                }
                else
                {
                    OnLogMessage($"[시스템 프록시 경고] 시스템 프록시 자동 설정 실패. 수동으로 설정하세요: {proxyServer}");
                    OnLogMessage($"[시스템 프록시 안내] Windows 설정 → 네트워크 및 인터넷 → 프록시에서 수동으로 설정하세요");
                }

                UpdateMenuItems();
                UpdateToggleButton();
                notifyIcon1.Icon = SystemIcons.Shield;
                notifyIcon1.Text = $"Proxy Client A - 실행 중 (포트: {_proxyPort})";
                UpdateStatusLabel();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"프록시 시작 실패: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task StopProxy()
        {
            try
            {
                if (_proxyClient != null)
                {
                    _proxyClient.LogMessage -= ProxyClient_LogMessage;
                    _proxyClient.ConnectionStatusChanged -= ProxyClient_ConnectionStatusChanged;
                    await _proxyClient.StopAsync();
                    _proxyClient.Dispose();
                    _proxyClient = null;
                }

                // Windows 시스템 프록시 비활성화
                if (SystemProxyHelper.DisableSystemProxy())
                {
                    OnLogMessage("[시스템 프록시] Windows 시스템 프록시가 비활성화되었습니다");
                }

                _isRunning = false;
                UpdateMenuItems();
                UpdateToggleButton();
                notifyIcon1.Icon = SystemIcons.Application;
                notifyIcon1.Text = "Proxy Client A - 중지됨";
                UpdateStatusLabel();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"프록시 중지 실패: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnLogMessage(string message)
        {
            if (_logForm != null && !_logForm.IsDisposed)
            {
                _logForm.AddLog(message);
            }
            System.Diagnostics.Debug.WriteLine(message);
        }

        private void ProxyClient_LogMessage(object sender, string message)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<object, string>(ProxyClient_LogMessage), sender, message);
                return;
            }

            // 로그 폼에 표시
            if (_logForm != null && !_logForm.IsDisposed)
            {
                _logForm.AddLog(message);
            }

            // 디버그 출력
            System.Diagnostics.Debug.WriteLine(message);
        }

        private void ProxyClient_ConnectionStatusChanged(object sender, bool connected)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<object, bool>(ProxyClient_ConnectionStatusChanged), sender, connected);
                return;
            }

            if (connected)
            {
                notifyIcon1.Icon = SystemIcons.Shield;
                notifyIcon1.Text = $"Proxy Client A - 연결됨 (포트: {_proxyPort})";
            }
            else
            {
                notifyIcon1.Icon = SystemIcons.Warning;
                notifyIcon1.Text = $"Proxy Client A - 연결 끊김 (포트: {_proxyPort})";
            }
        }

        private void UpdateMenuItems()
        {
            시작ToolStripMenuItem.Enabled = !_isRunning;
            중지ToolStripMenuItem.Enabled = _isRunning;
        }

        private void UpdateToggleButton()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(UpdateToggleButton));
                return;
            }

            btnToggle.Text = _isRunning ? "중지" : "시작";
            btnToggle.BackColor = _isRunning ? System.Drawing.Color.FromArgb(255, 192, 192) : System.Drawing.Color.FromArgb(192, 255, 192);
        }

        private void ShowSettingsForm()
        {
            using (var settingsForm = new SettingsForm(_relayServerUrl, _proxyPort, _domainFilter, _accessToken))
            {
                if (settingsForm.ShowDialog() == DialogResult.OK)
                {
                    _relayServerUrl = settingsForm.RelayServerUrl;
                    _proxyPort = settingsForm.ProxyPort;
                    _domainFilter = settingsForm.DomainFilter;
                    _accessToken = settingsForm.AccessToken;

                    if (_isRunning)
                    {
                        StopProxy().Wait(2000);
                        StartProxy().Wait(2000);
                    }
                }
            }
        }

        private void UpdateStatusLabel()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(UpdateStatusLabel));
                return;
            }

            string status = _isRunning ? "실행 중" : "중지됨";
            string filterInfo = string.IsNullOrEmpty(_domainFilter) ? "모든 도메인" : $"필터링: {_domainFilter}";
            lblStatus.Text = $"상태: {status} | 포트: {_proxyPort} | {filterInfo}";
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
            }
            else
            {
                // 프로그램 종료 시 프록시 중지 및 시스템 프록시 비활성화
                if (_isRunning)
                {
                    StopProxy().Wait(3000);
                }
                else
                {
                    // 실행 중이 아니어도 시스템 프록시는 비활성화 (안전장치)
                    SystemProxyHelper.DisableSystemProxy();
                }
                
                if (_logForm != null && !_logForm.IsDisposed)
                {
                    _logForm.Close();
                }
            }
            base.OnFormClosing(e);
        }
        
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // 최종 안전장치: 프로그램이 종료될 때 시스템 프록시 비활성화
            try
            {
                if (_isRunning)
                {
                    SystemProxyHelper.DisableSystemProxy();
                }
                else
                {
                    // 실행 중이 아니어도 시스템 프록시는 비활성화
                    SystemProxyHelper.DisableSystemProxy();
                }
            }
            catch { }
            
            base.OnFormClosed(e);
        }
    }
}
