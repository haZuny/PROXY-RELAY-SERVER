namespace FroxyServerClient
{
    partial class Form1
    {
        /// <summary>
        /// 필수 디자이너 변수입니다.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 사용 중인 모든 리소스를 정리합니다.
        /// </summary>
        /// <param name="disposing">관리되는 리소스를 삭제해야 하면 true이고, 그렇지 않으면 false입니다.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form 디자이너에서 생성한 코드

        /// <summary>
        /// 디자이너 지원에 필요한 메서드입니다. 
        /// 이 메서드의 내용을 코드 편집기로 수정하지 마세요.
        /// </summary>
        private void InitializeComponent()
        {
            this.lblTitle = new System.Windows.Forms.Label();
            this.lblWorkPcIp = new System.Windows.Forms.Label();
            this.txtWorkPcIp = new System.Windows.Forms.TextBox();
            this.lblPort = new System.Windows.Forms.Label();
            this.txtPort = new System.Windows.Forms.TextBox();
            this.lblStatus = new System.Windows.Forms.Label();
            this.lblStatusValue = new System.Windows.Forms.Label();
            this.btnApply = new System.Windows.Forms.Button();
            this.btnDisable = new System.Windows.Forms.Button();
            this.groupBoxSettings = new System.Windows.Forms.GroupBox();
            this.groupBoxStatus = new System.Windows.Forms.GroupBox();
            this.lblDomains = new System.Windows.Forms.Label();
            this.txtDomains = new System.Windows.Forms.TextBox();
            this.groupBoxDomains = new System.Windows.Forms.GroupBox();
            this.groupBoxSettings.SuspendLayout();
            this.groupBoxStatus.SuspendLayout();
            this.groupBoxDomains.SuspendLayout();
            this.SuspendLayout();
            // 
            // lblTitle
            // 
            this.lblTitle.AutoSize = true;
            this.lblTitle.Font = new System.Drawing.Font("맑은 고딕", 14F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(129)));
            this.lblTitle.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(58)))), ((int)(((byte)(138)))));
            this.lblTitle.Location = new System.Drawing.Point(23, 25);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(271, 32);
            this.lblTitle.TabIndex = 0;
            this.lblTitle.Text = "프록시 클라이언트 설정";
            // 
            // lblWorkPcIp
            // 
            this.lblWorkPcIp.AutoSize = true;
            this.lblWorkPcIp.Font = new System.Drawing.Font("맑은 고딕", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(129)));
            this.lblWorkPcIp.Location = new System.Drawing.Point(23, 38);
            this.lblWorkPcIp.Name = "lblWorkPcIp";
            this.lblWorkPcIp.Size = new System.Drawing.Size(81, 20);
            this.lblWorkPcIp.TabIndex = 1;
            this.lblWorkPcIp.Text = "작업PC IP:";
            // 
            // txtWorkPcIp
            // 
            this.txtWorkPcIp.Font = new System.Drawing.Font("맑은 고딕", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(129)));
            this.txtWorkPcIp.Location = new System.Drawing.Point(23, 62);
            this.txtWorkPcIp.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.txtWorkPcIp.Name = "txtWorkPcIp";
            this.txtWorkPcIp.Size = new System.Drawing.Size(365, 27);
            this.txtWorkPcIp.TabIndex = 2;
            this.txtWorkPcIp.Text = "192.168.1.100";
            // 
            // lblPort
            // 
            this.lblPort.AutoSize = true;
            this.lblPort.Font = new System.Drawing.Font("맑은 고딕", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(129)));
            this.lblPort.Location = new System.Drawing.Point(23, 106);
            this.lblPort.Name = "lblPort";
            this.lblPort.Size = new System.Drawing.Size(43, 20);
            this.lblPort.TabIndex = 3;
            this.lblPort.Text = "포트:";
            // 
            // txtPort
            // 
            this.txtPort.Font = new System.Drawing.Font("맑은 고딕", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(129)));
            this.txtPort.Location = new System.Drawing.Point(23, 131);
            this.txtPort.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.txtPort.Name = "txtPort";
            this.txtPort.Size = new System.Drawing.Size(365, 27);
            this.txtPort.TabIndex = 4;
            this.txtPort.Text = "9999";
            // 
            // lblStatus
            // 
            this.lblStatus.AutoSize = true;
            this.lblStatus.Font = new System.Drawing.Font("맑은 고딕", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(129)));
            this.lblStatus.Location = new System.Drawing.Point(23, 38);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(43, 20);
            this.lblStatus.TabIndex = 5;
            this.lblStatus.Text = "상태:";
            // 
            // lblStatusValue
            // 
            this.lblStatusValue.AutoSize = true;
            this.lblStatusValue.Font = new System.Drawing.Font("맑은 고딕", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(129)));
            this.lblStatusValue.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(22)))), ((int)(((byte)(101)))), ((int)(((byte)(52)))));
            this.lblStatusValue.Location = new System.Drawing.Point(23, 69);
            this.lblStatusValue.Name = "lblStatusValue";
            this.lblStatusValue.Size = new System.Drawing.Size(134, 20);
            this.lblStatusValue.TabIndex = 6;
            this.lblStatusValue.Text = "프록시 비활성화됨";
            // 
            // btnApply
            // 
            this.btnApply.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(59)))), ((int)(((byte)(130)))), ((int)(((byte)(246)))));
            this.btnApply.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnApply.Font = new System.Drawing.Font("맑은 고딕", 10F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(129)));
            this.btnApply.ForeColor = System.Drawing.Color.White;
            this.btnApply.Location = new System.Drawing.Point(23, 562);
            this.btnApply.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.btnApply.Name = "btnApply";
            this.btnApply.Size = new System.Drawing.Size(200, 45);
            this.btnApply.TabIndex = 7;
            this.btnApply.Text = "적용";
            this.btnApply.UseVisualStyleBackColor = false;
            this.btnApply.Click += new System.EventHandler(this.btnApply_Click);
            // 
            // btnDisable
            // 
            this.btnDisable.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(239)))), ((int)(((byte)(68)))), ((int)(((byte)(68)))));
            this.btnDisable.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnDisable.Font = new System.Drawing.Font("맑은 고딕", 10F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(129)));
            this.btnDisable.ForeColor = System.Drawing.Color.White;
            this.btnDisable.Location = new System.Drawing.Point(234, 562);
            this.btnDisable.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.btnDisable.Name = "btnDisable";
            this.btnDisable.Size = new System.Drawing.Size(200, 45);
            this.btnDisable.TabIndex = 8;
            this.btnDisable.Text = "해제";
            this.btnDisable.UseVisualStyleBackColor = false;
            this.btnDisable.Click += new System.EventHandler(this.btnDisable_Click);
            // 
            // groupBoxSettings
            // 
            this.groupBoxSettings.Controls.Add(this.txtPort);
            this.groupBoxSettings.Controls.Add(this.lblPort);
            this.groupBoxSettings.Controls.Add(this.txtWorkPcIp);
            this.groupBoxSettings.Controls.Add(this.lblWorkPcIp);
            this.groupBoxSettings.Font = new System.Drawing.Font("맑은 고딕", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(129)));
            this.groupBoxSettings.Location = new System.Drawing.Point(23, 75);
            this.groupBoxSettings.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.groupBoxSettings.Name = "groupBoxSettings";
            this.groupBoxSettings.Padding = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.groupBoxSettings.Size = new System.Drawing.Size(411, 188);
            this.groupBoxSettings.TabIndex = 9;
            this.groupBoxSettings.TabStop = false;
            this.groupBoxSettings.Text = "프록시 서버 설정";
            // 
            // groupBoxStatus
            // 
            this.groupBoxStatus.Controls.Add(this.lblStatusValue);
            this.groupBoxStatus.Controls.Add(this.lblStatus);
            this.groupBoxStatus.Font = new System.Drawing.Font("맑은 고딕", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(129)));
            this.groupBoxStatus.Location = new System.Drawing.Point(23, 420);
            this.groupBoxStatus.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.groupBoxStatus.Name = "groupBoxStatus";
            this.groupBoxStatus.Padding = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.groupBoxStatus.Size = new System.Drawing.Size(411, 102);
            this.groupBoxStatus.TabIndex = 10;
            this.groupBoxStatus.TabStop = false;
            this.groupBoxStatus.Text = "현재 상태";
            // 
            // lblDomains
            // 
            this.lblDomains.AutoSize = true;
            this.lblDomains.Font = new System.Drawing.Font("맑은 고딕", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(129)));
            this.lblDomains.Location = new System.Drawing.Point(23, 38);
            this.lblDomains.Name = "lblDomains";
            this.lblDomains.Size = new System.Drawing.Size(385, 20);
            this.lblDomains.TabIndex = 11;
            this.lblDomains.Text = "프록시로 보낼 도메인 (쉼표로 구분, 예: *.hospital.local)";
            // 
            // txtDomains
            // 
            this.txtDomains.Font = new System.Drawing.Font("맑은 고딕", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(129)));
            this.txtDomains.Location = new System.Drawing.Point(23, 62);
            this.txtDomains.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.txtDomains.Multiline = true;
            this.txtDomains.Name = "txtDomains";
            this.txtDomains.Size = new System.Drawing.Size(365, 60);
            this.txtDomains.TabIndex = 12;
            this.txtDomains.Text = "*.hospital.local, 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16";
            // 
            // groupBoxDomains
            // 
            this.groupBoxDomains.Controls.Add(this.txtDomains);
            this.groupBoxDomains.Controls.Add(this.lblDomains);
            this.groupBoxDomains.Font = new System.Drawing.Font("맑은 고딕", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(129)));
            this.groupBoxDomains.Location = new System.Drawing.Point(23, 270);
            this.groupBoxDomains.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.groupBoxDomains.Name = "groupBoxDomains";
            this.groupBoxDomains.Padding = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.groupBoxDomains.Size = new System.Drawing.Size(411, 140);
            this.groupBoxDomains.TabIndex = 13;
            this.groupBoxDomains.TabStop = false;
            this.groupBoxDomains.Text = "도메인 필터 설정";
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.White;
            this.ClientSize = new System.Drawing.Size(457, 620);
            this.Controls.Add(this.groupBoxDomains);
            this.Controls.Add(this.groupBoxStatus);
            this.Controls.Add(this.groupBoxSettings);
            this.Controls.Add(this.btnDisable);
            this.Controls.Add(this.btnApply);
            this.Controls.Add(this.lblTitle);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "Form1";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Froxy 프록시 클라이언트";
            this.Load += new System.EventHandler(this.Form1_Load);
            this.groupBoxSettings.ResumeLayout(false);
            this.groupBoxSettings.PerformLayout();
            this.groupBoxStatus.ResumeLayout(false);
            this.groupBoxStatus.PerformLayout();
            this.groupBoxDomains.ResumeLayout(false);
            this.groupBoxDomains.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.Label lblWorkPcIp;
        private System.Windows.Forms.TextBox txtWorkPcIp;
        private System.Windows.Forms.Label lblPort;
        private System.Windows.Forms.TextBox txtPort;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.Label lblStatusValue;
        private System.Windows.Forms.Button btnApply;
        private System.Windows.Forms.Button btnDisable;
        private System.Windows.Forms.GroupBox groupBoxSettings;
        private System.Windows.Forms.GroupBox groupBoxStatus;
        private System.Windows.Forms.Label lblDomains;
        private System.Windows.Forms.TextBox txtDomains;
        private System.Windows.Forms.GroupBox groupBoxDomains;
    }
}

