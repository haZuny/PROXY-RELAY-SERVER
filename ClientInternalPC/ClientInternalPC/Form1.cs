using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ClientInternalPC
{
    public partial class Form1 : Form
    {
        private AgentClientB _agent;
        private NotifyIcon _notifyIcon;
        private ContextMenuStrip _contextMenu;
        private ToolStripMenuItem _startMenuItem;
        private ToolStripMenuItem _stopMenuItem;
        private ToolStripMenuItem _exitMenuItem;

        public Form1()
        {
            InitializeComponent();
            InitializeTrayIcon();
            InitializeAgent();
        }

        private void InitializeTrayIcon()
        {
            // 컨텍스트 메뉴 생성
            _contextMenu = new ContextMenuStrip();
            
            _startMenuItem = new ToolStripMenuItem("시작");
            _startMenuItem.Click += StartMenuItem_Click;
            _contextMenu.Items.Add(_startMenuItem);

            _stopMenuItem = new ToolStripMenuItem("중지");
            _stopMenuItem.Click += StopMenuItem_Click;
            _stopMenuItem.Enabled = false;
            _contextMenu.Items.Add(_stopMenuItem);

            _contextMenu.Items.Add(new ToolStripSeparator());

            _exitMenuItem = new ToolStripMenuItem("종료");
            _exitMenuItem.Click += ExitMenuItem_Click;
            _contextMenu.Items.Add(_exitMenuItem);

            // 트레이 아이콘 생성
            _notifyIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "Client B - 내부망 에이전트",
                ContextMenuStrip = _contextMenu,
                Visible = true
            };

            _notifyIcon.DoubleClick += NotifyIcon_DoubleClick;
        }

        private void InitializeAgent()
        {
            _agent = new AgentClientB();
            _agent.ConnectionStatusChanged += Agent_ConnectionStatusChanged;
            _agent.LogMessage += Agent_LogMessage;
        }

        private void NotifyIcon_DoubleClick(object sender, EventArgs e)
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.BringToFront();
        }

        private async void StartMenuItem_Click(object sender, EventArgs e)
        {
            await StartAgent();
        }

        private async void StopMenuItem_Click(object sender, EventArgs e)
        {
            await StopAgent();
        }

        private async void btnStart_Click(object sender, EventArgs e)
        {
            await StartAgent();
        }

        private async void btnStop_Click(object sender, EventArgs e)
        {
            await StopAgent();
        }

        private async Task StartAgent()
        {
            _startMenuItem.Enabled = false;
            _stopMenuItem.Enabled = true;
            btnStart.Enabled = false;
            btnStop.Enabled = true;
            _notifyIcon.Text = "Client B - 연결 중...";
            
            await Task.Run(() => _agent.StartAsync());
        }

        private async Task StopAgent()
        {
            _startMenuItem.Enabled = true;
            _stopMenuItem.Enabled = false;
            btnStart.Enabled = true;
            btnStop.Enabled = false;
            _notifyIcon.Text = "Client B - 중지됨";
            
            await _agent.StopAsync();
        }

        private void ExitMenuItem_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("프로그램을 종료하시겠습니까?", "종료 확인", 
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _agent?.StopAsync().Wait();
                _notifyIcon.Visible = false;
                Application.Exit();
            }
        }

        private void Agent_ConnectionStatusChanged(object sender, bool isConnected)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action<object, bool>(Agent_ConnectionStatusChanged), sender, isConnected);
                return;
            }

            if (isConnected)
            {
                _notifyIcon.Icon = SystemIcons.Shield;
                _notifyIcon.Text = "Client B - 연결됨";
                lblStatus.Text = "연결됨";
                lblStatus.ForeColor = Color.Green;
            }
            else
            {
                _notifyIcon.Icon = SystemIcons.Warning;
                _notifyIcon.Text = "Client B - 연결 끊김";
                lblStatus.Text = "연결 끊김";
                lblStatus.ForeColor = Color.Red;
            }
        }

        private void Agent_LogMessage(object sender, string message)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action<object, string>(Agent_LogMessage), sender, message);
                return;
            }

            // 로그 텍스트박스에 추가
            txtLog.AppendText(message + Environment.NewLine);
            txtLog.SelectionStart = txtLog.Text.Length;
            txtLog.ScrollToCaret();

            // 최대 1000줄 유지
            if (txtLog.Lines.Length > 1000)
            {
                var lines = txtLog.Lines;
                var newLines = new string[1000];
                Array.Copy(lines, lines.Length - 1000, newLines, 0, 1000);
                txtLog.Lines = newLines;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
                _notifyIcon.ShowBalloonTip(2000, "Client B", "프로그램이 트레이로 최소화되었습니다.", ToolTipIcon.Info);
            }
            base.OnFormClosing(e);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            this.WindowState = FormWindowState.Minimized;
            this.Hide();
        }

        /// <summary>
        /// 추가 리소스 정리 (Form1.Designer.cs의 Dispose에서 호출됨)
        /// </summary>
        partial void CleanupResources()
        {
            _agent?.StopAsync().Wait();
            _notifyIcon?.Dispose();
            _contextMenu?.Dispose();
        }
    }
}
