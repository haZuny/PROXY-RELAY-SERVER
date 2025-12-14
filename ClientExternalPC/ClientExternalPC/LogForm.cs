using System;
using System.Drawing;
using System.Windows.Forms;

namespace ClientExternalPC
{
    public partial class LogForm : Form
    {
        private TextBox txtLog;
        private Button btnClear;
        private CheckBox chkAutoScroll;
        private Label lblLogCount;

        public LogForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.txtLog = new TextBox();
            this.btnClear = new Button();
            this.chkAutoScroll = new CheckBox();
            this.lblLogCount = new Label();

            this.SuspendLayout();

            // txtLog
            this.txtLog.Anchor = ((AnchorStyles)((((AnchorStyles.Top | AnchorStyles.Bottom) | AnchorStyles.Left) | AnchorStyles.Right)));
            this.txtLog.Font = new Font("Consolas", 9F);
            this.txtLog.Location = new Point(12, 12);
            this.txtLog.Multiline = true;
            this.txtLog.Name = "txtLog";
            this.txtLog.ReadOnly = true;
            this.txtLog.ScrollBars = ScrollBars.Vertical;
            this.txtLog.Size = new Size(760, 500);
            this.txtLog.TabIndex = 0;

            // btnClear
            this.btnClear.Anchor = ((AnchorStyles)((AnchorStyles.Bottom | AnchorStyles.Right)));
            this.btnClear.Location = new Point(697, 520);
            this.btnClear.Name = "btnClear";
            this.btnClear.Size = new Size(75, 23);
            this.btnClear.TabIndex = 1;
            this.btnClear.Text = "지우기";
            this.btnClear.UseVisualStyleBackColor = true;
            this.btnClear.Click += BtnClear_Click;

            // chkAutoScroll
            this.chkAutoScroll.Anchor = ((AnchorStyles)((AnchorStyles.Bottom | AnchorStyles.Left)));
            this.chkAutoScroll.AutoSize = true;
            this.chkAutoScroll.Checked = true;
            this.chkAutoScroll.CheckState = CheckState.Checked;
            this.chkAutoScroll.Location = new Point(12, 524);
            this.chkAutoScroll.Name = "chkAutoScroll";
            this.chkAutoScroll.Size = new Size(82, 16);
            this.chkAutoScroll.TabIndex = 2;
            this.chkAutoScroll.Text = "자동 스크롤";
            this.chkAutoScroll.UseVisualStyleBackColor = true;

            // lblLogCount
            this.lblLogCount.Anchor = ((AnchorStyles)((AnchorStyles.Bottom | AnchorStyles.Left)));
            this.lblLogCount.AutoSize = true;
            this.lblLogCount.Location = new Point(100, 525);
            this.lblLogCount.Name = "lblLogCount";
            this.lblLogCount.Size = new Size(60, 12);
            this.lblLogCount.Text = "로그: 0개";

            // LogForm
            this.AutoScaleDimensions = new SizeF(7F, 12F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(784, 555);
            this.Controls.Add(this.txtLog);
            this.Controls.Add(this.btnClear);
            this.Controls.Add(this.chkAutoScroll);
            this.Controls.Add(this.lblLogCount);
            this.MinimumSize = new Size(400, 300);
            this.Name = "LogForm";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Text = "프록시 로그";
            this.FormClosing += LogForm_FormClosing;

            this.ResumeLayout(false);
            this.PerformLayout();
        }

        public void AddLog(string message)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string>(AddLog), message);
                return;
            }

            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var logEntry = $"[{timestamp}] {message}\r\n";

            txtLog.AppendText(logEntry);

            // 로그 개수 업데이트
            var lineCount = txtLog.Lines.Length;
            lblLogCount.Text = $"로그: {lineCount}개";

            // 자동 스크롤
            if (chkAutoScroll.Checked)
            {
                txtLog.SelectionStart = txtLog.Text.Length;
                txtLog.ScrollToCaret();
            }
        }

        private void BtnClear_Click(object sender, EventArgs e)
        {
            txtLog.Clear();
            lblLogCount.Text = "로그: 0개";
        }

        private void LogForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            // 창을 닫을 때 숨기기만 하고 실제로 닫지 않음
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
            }
        }
    }
}

