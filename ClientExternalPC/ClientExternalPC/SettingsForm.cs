using System;
using System.Windows.Forms;

namespace ClientExternalPC
{
    public partial class SettingsForm : Form
    {
        public string RelayServerUrl { get; private set; }  // http://localhost:8080
        public int ProxyPort { get; private set; }
        public string DomainFilter { get; private set; }
        public string AccessToken { get; private set; }

        private TextBox txtRelayServerUrl;
        private TextBox txtProxyPort;
        private TextBox txtDomainFilter;
        private TextBox txtAccessToken;
        private Button btnOK;
        private Button btnCancel;
        private Label lblRelayServerUrl;
        private Label lblProxyPort;
        private Label lblDomainFilter;
        private Label lblDomainFilterHint;
        private Label lblAccessToken;
        private Label lblTokenHint;

        public SettingsForm(string currentRelayServerUrl, int currentProxyPort, string currentDomainFilter = "", string currentAccessToken = "")
        {
            RelayServerUrl = currentRelayServerUrl ?? "http://localhost:8080";
            ProxyPort = currentProxyPort;
            DomainFilter = currentDomainFilter ?? "";
            AccessToken = currentAccessToken ?? "default-token-change-in-production";
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.lblRelayServerUrl = new Label();
            this.txtRelayServerUrl = new TextBox();
            this.lblProxyPort = new Label();
            this.txtProxyPort = new TextBox();
            this.lblDomainFilter = new Label();
            this.txtDomainFilter = new TextBox();
            this.lblDomainFilterHint = new Label();
            this.lblAccessToken = new Label();
            this.txtAccessToken = new TextBox();
            this.lblTokenHint = new Label();
            this.btnOK = new Button();
            this.btnCancel = new Button();

            this.SuspendLayout();

            // lblRelayServerUrl
            this.lblRelayServerUrl.AutoSize = true;
            this.lblRelayServerUrl.Location = new System.Drawing.Point(12, 15);
            this.lblRelayServerUrl.Name = "lblRelayServerUrl";
            this.lblRelayServerUrl.Size = new System.Drawing.Size(70, 12);
            this.lblRelayServerUrl.Text = "Relay 서버:";

            // txtRelayServerUrl
            this.txtRelayServerUrl.Location = new System.Drawing.Point(90, 12);
            this.txtRelayServerUrl.Size = new System.Drawing.Size(350, 21);
            this.txtRelayServerUrl.Text = RelayServerUrl;

            // lblProxyPort
            this.lblProxyPort.AutoSize = true;
            this.lblProxyPort.Location = new System.Drawing.Point(12, 45);
            this.lblProxyPort.Name = "lblProxyPort";
            this.lblProxyPort.Size = new System.Drawing.Size(70, 12);
            this.lblProxyPort.Text = "프록시 포트:";

            // txtProxyPort
            this.txtProxyPort.Location = new System.Drawing.Point(90, 42);
            this.txtProxyPort.Size = new System.Drawing.Size(100, 21);
            this.txtProxyPort.Text = ProxyPort.ToString();

            // lblDomainFilter
            this.lblDomainFilter.AutoSize = true;
            this.lblDomainFilter.Location = new System.Drawing.Point(12, 75);
            this.lblDomainFilter.Name = "lblDomainFilter";
            this.lblDomainFilter.Size = new System.Drawing.Size(70, 12);
            this.lblDomainFilter.Text = "도메인 필터:";

            // txtDomainFilter
            this.txtDomainFilter.Location = new System.Drawing.Point(90, 72);
            this.txtDomainFilter.Size = new System.Drawing.Size(350, 21);
            this.txtDomainFilter.Text = DomainFilter;

            // lblDomainFilterHint
            this.lblDomainFilterHint.AutoSize = true;
            this.lblDomainFilterHint.Font = new System.Drawing.Font("맑은 고딕", 8F);
            this.lblDomainFilterHint.ForeColor = System.Drawing.Color.Gray;
            this.lblDomainFilterHint.Location = new System.Drawing.Point(90, 95);
            this.lblDomainFilterHint.Name = "lblDomainFilterHint";
            this.lblDomainFilterHint.Size = new System.Drawing.Size(350, 26);
            this.lblDomainFilterHint.Text = "콤마(,) 또는 세미콜론(;)으로 구분. 빈 값이면 모든 도메인 허용";

            // lblAccessToken
            this.lblAccessToken.AutoSize = true;
            this.lblAccessToken.Location = new System.Drawing.Point(12, 125);
            this.lblAccessToken.Name = "lblAccessToken";
            this.lblAccessToken.Size = new System.Drawing.Size(70, 12);
            this.lblAccessToken.Text = "액세스 토큰:";

            // txtAccessToken
            this.txtAccessToken.Location = new System.Drawing.Point(90, 122);
            this.txtAccessToken.Size = new System.Drawing.Size(350, 21);
            this.txtAccessToken.Text = AccessToken;

            // lblTokenHint
            this.lblTokenHint.AutoSize = true;
            this.lblTokenHint.Font = new System.Drawing.Font("맑은 고딕", 8F);
            this.lblTokenHint.ForeColor = System.Drawing.Color.Gray;
            this.lblTokenHint.Location = new System.Drawing.Point(90, 145);
            this.lblTokenHint.Name = "lblTokenHint";
            this.lblTokenHint.Size = new System.Drawing.Size(350, 26);
            this.lblTokenHint.Text = "Relay Server에 설정된 액세스 토큰을 입력하세요";

            // btnOK
            this.btnOK.DialogResult = DialogResult.OK;
            this.btnOK.Location = new System.Drawing.Point(284, 175);
            this.btnOK.Size = new System.Drawing.Size(75, 23);
            this.btnOK.Text = "확인";
            this.btnOK.UseVisualStyleBackColor = true;
            this.btnOK.Click += BtnOK_Click;

            // btnCancel
            this.btnCancel.DialogResult = DialogResult.Cancel;
            this.btnCancel.Location = new System.Drawing.Point(365, 175);
            this.btnCancel.Size = new System.Drawing.Size(75, 23);
            this.btnCancel.Text = "취소";
            this.btnCancel.UseVisualStyleBackColor = true;

            // SettingsForm
            this.AcceptButton = this.btnOK;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(452, 210);
            this.Controls.Add(this.lblRelayServerUrl);
            this.Controls.Add(this.txtRelayServerUrl);
            this.Controls.Add(this.lblProxyPort);
            this.Controls.Add(this.txtProxyPort);
            this.Controls.Add(this.lblDomainFilter);
            this.Controls.Add(this.txtDomainFilter);
            this.Controls.Add(this.lblDomainFilterHint);
            this.Controls.Add(this.lblAccessToken);
            this.Controls.Add(this.txtAccessToken);
            this.Controls.Add(this.lblTokenHint);
            this.Controls.Add(this.btnOK);
            this.Controls.Add(this.btnCancel);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Text = "프록시 설정";

            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtRelayServerUrl.Text))
            {
                MessageBox.Show("Relay 서버 주소를 입력하세요.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var serverUrl = txtRelayServerUrl.Text.Trim();
            if (!serverUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
                !serverUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                serverUrl = "http://" + serverUrl;
            }

            if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out Uri uri))
            {
                MessageBox.Show("올바른 서버 주소를 입력하세요. (예: http://localhost:8080)", "오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!int.TryParse(txtProxyPort.Text, out int port) || port < 1 || port > 65535)
            {
                MessageBox.Show("올바른 포트 번호를 입력하세요. (1-65535)", "오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(txtAccessToken.Text))
            {
                MessageBox.Show("액세스 토큰을 입력하세요.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            RelayServerUrl = serverUrl;
            ProxyPort = port;
            DomainFilter = txtDomainFilter.Text.Trim();
            AccessToken = txtAccessToken.Text.Trim();
        }
    }
}

