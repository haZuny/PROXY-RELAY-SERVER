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

        public Form1()
        {
            InitializeComponent();
            InitializeTrayIcon();
            this.WindowState = FormWindowState.Normal;
            this.ShowInTaskbar = true;
            
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

        private void 종료ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (_isRunning)
            {
                StopProxy().Wait(3000);
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

        private void ProxyClient_LogMessage(object sender, string message)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<object, string>(ProxyClient_LogMessage), sender, message);
                return;
            }

            // 로그를 리스트박스에 추가하거나 파일에 기록
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
                if (_isRunning)
                {
                    StopProxy().Wait(3000);
                }
            }
            base.OnFormClosing(e);
        }
    }
}
