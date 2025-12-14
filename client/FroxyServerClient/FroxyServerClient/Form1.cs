using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;

namespace FroxyServerClient
{
    public partial class Form1 : Form
    {
        private PacFileGenerator _pacGenerator;
        private ProxyManager _proxyManager;
        private string _settingsFilePath;

        public Form1()
        {
            InitializeComponent();
            _pacGenerator = new PacFileGenerator();
            _proxyManager = new ProxyManager();
            _settingsFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FroxyServerClient",
                "settings.txt"
            );
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            LoadSettings();
            UpdateProxyStatus();
        }

        /// <summary>
        /// 설정 파일에서 저장된 값 로드
        /// </summary>
        private void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    string[] lines = File.ReadAllLines(_settingsFilePath);
                    if (lines.Length >= 2)
                    {
                        txtWorkPcIp.Text = lines[0];
                        txtPort.Text = lines[1];
                    }
                    if (lines.Length >= 3)
                    {
                        txtDomains.Text = lines[2];
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"설정 로드 실패: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// 현재 설정을 파일에 저장
        /// </summary>
        private void SaveSettings()
        {
            try
            {
                string directory = Path.GetDirectoryName(_settingsFilePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllLines(_settingsFilePath, new[]
                {
                    txtWorkPcIp.Text,
                    txtPort.Text,
                    txtDomains.Text
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"설정 저장 실패: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// 프록시 상태 업데이트
        /// </summary>
        private void UpdateProxyStatus()
        {
            bool isEnabled = _proxyManager.IsProxyEnabled();
            if (isEnabled)
            {
                lblStatusValue.Text = "✅ 프록시 활성화됨";
                lblStatusValue.ForeColor = Color.FromArgb(22, 101, 52);
            }
            else
            {
                lblStatusValue.Text = "프록시 비활성화됨";
                lblStatusValue.ForeColor = Color.FromArgb(107, 114, 128);
            }
        }

        /// <summary>
        /// 적용 버튼 클릭 이벤트
        /// </summary>
        private void btnApply_Click(object sender, EventArgs e)
        {
            // 입력 검증
            if (string.IsNullOrWhiteSpace(txtWorkPcIp.Text))
            {
                MessageBox.Show("작업PC IP를 입력해주세요.", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtWorkPcIp.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(txtPort.Text))
            {
                MessageBox.Show("포트를 입력해주세요.", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtPort.Focus();
                return;
            }

            if (!int.TryParse(txtPort.Text, out int port) || port <= 0 || port > 65535)
            {
                MessageBox.Show("유효한 포트 번호를 입력해주세요. (1-65535)", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtPort.Focus();
                return;
            }

            try
            {
                // 설정 저장
                SaveSettings();

                // PAC 파일 생성 (도메인 필터 포함)
                string pacFilePath = _pacGenerator.GeneratePacFile(
                    txtWorkPcIp.Text.Trim(), 
                    port, 
                    txtDomains.Text.Trim()
                );

                // 프록시 설정 적용
                bool success = _proxyManager.SetProxyWithPac(pacFilePath);

                if (success)
                {
                    MessageBox.Show(
                        $"프록시 설정이 적용되었습니다.\n\n" +
                        $"작업PC: {txtWorkPcIp.Text}\n" +
                        $"포트: {port}\n\n" +
                        $"이제 내부망 도메인(*.hospital.local)으로의 요청이\n" +
                        $"프록시 서버를 통해 전달됩니다.",
                        "성공",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );
                    UpdateProxyStatus();
                }
                else
                {
                    MessageBox.Show("프록시 설정 적용에 실패했습니다.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"프록시 설정 중 오류가 발생했습니다:\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 해제 버튼 클릭 이벤트
        /// </summary>
        private void btnDisable_Click(object sender, EventArgs e)
        {
            DialogResult result = MessageBox.Show(
                "프록시 설정을 해제하시겠습니까?",
                "확인",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );

            if (result == DialogResult.Yes)
            {
                try
                {
                    bool success = _proxyManager.DisableProxy();

                    if (success)
                    {
                        // PAC 파일 삭제
                        _pacGenerator.DeletePacFile();

                        MessageBox.Show("프록시 설정이 해제되었습니다.", "성공", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        UpdateProxyStatus();
                    }
                    else
                    {
                        MessageBox.Show("프록시 해제에 실패했습니다.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"프록시 해제 중 오류가 발생했습니다:\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
