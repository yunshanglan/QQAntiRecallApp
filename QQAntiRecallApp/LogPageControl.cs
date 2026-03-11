using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QQAntiRecallApp
{
    public partial class LogPageControl : UserControl
    {
        private RichTextBox txtLog;
        private Label lblListenStatus, lblDbStatus, lblUptime, lblMsgCount;
        private Button btnClearLog, btnRefreshStatus, btnExportLog;
        private System.Windows.Forms.Timer statusTimer;
        private DateTime startTime;

        private readonly Func<Task<bool>> _testConnectionAsync;
        private readonly Func<Task<int>> _getTodayCountAsync;
        private readonly Func<bool> _getListeningStatus;

        public LogPageControl(Func<Task<bool>> testConnectionAsync, Func<Task<int>> getTodayCountAsync, Func<bool> getListeningStatus)
        {
            _testConnectionAsync = testConnectionAsync;
            _getTodayCountAsync = getTodayCountAsync;
            _getListeningStatus = getListeningStatus;

            InitializeComponent();
            Logger.OnLogWritten += AppendLogToUI;
            startTime = DateTime.Now;
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            // 使用 TableLayoutPanel 作为主布局容器
            var mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(10)
            };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 日志区域占满剩余空间
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); // 按钮面板固定高度
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100)); // 状态面板固定高度
            this.Controls.Add(mainLayout);

            // 日志显示区域（放在第一行）
            txtLog = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.Black,
                ForeColor = Color.LimeGreen,
                Font = new Font("Consolas", 9),
                WordWrap = false,
                ScrollBars = RichTextBoxScrollBars.Vertical
            };
            mainLayout.Controls.Add(txtLog, 0, 0);

            // 按钮面板（第二行）
            Panel buttonPanel = new Panel { Dock = DockStyle.Fill };
            mainLayout.Controls.Add(buttonPanel, 0, 1);

            btnClearLog = new Button { Text = "清空日志", Location = new Point(0, 10), Size = new Size(100, 30) };
            btnClearLog.Click += (s, e) => txtLog.Clear();
            buttonPanel.Controls.Add(btnClearLog);

            btnRefreshStatus = new Button { Text = "刷新状态", Location = new Point(110, 10), Size = new Size(100, 30) };
            btnRefreshStatus.Click += async (s, e) => await RefreshStatusAsync();
            buttonPanel.Controls.Add(btnRefreshStatus);

            btnExportLog = new Button { Text = "导出日志", Location = new Point(220, 10), Size = new Size(100, 30) };
            btnExportLog.Click += BtnExportLog_Click;
            buttonPanel.Controls.Add(btnExportLog);

            // 状态面板（第三行）
            GroupBox statusBox = new GroupBox
            {
                Text = "系统状态",
                Dock = DockStyle.Fill
            };
            mainLayout.Controls.Add(statusBox, 0, 2);

            TableLayoutPanel statusTable = new TableLayoutPanel
            {
                ColumnCount = 4,
                RowCount = 2,
                Dock = DockStyle.Fill,
                Padding = new Padding(5)
            };
            statusTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            statusTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            statusTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            statusTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            statusBox.Controls.Add(statusTable);

            // 第一行
            statusTable.Controls.Add(new Label { Text = "监听状态:", TextAlign = ContentAlignment.MiddleRight }, 0, 0);
            lblListenStatus = new Label { Text = "未知", ForeColor = Color.Red, TextAlign = ContentAlignment.MiddleLeft };
            statusTable.Controls.Add(lblListenStatus, 1, 0);

            statusTable.Controls.Add(new Label { Text = "数据库:", TextAlign = ContentAlignment.MiddleRight }, 2, 0);
            lblDbStatus = new Label { Text = "未知", ForeColor = Color.Red, TextAlign = ContentAlignment.MiddleLeft };
            statusTable.Controls.Add(lblDbStatus, 3, 0);

            // 第二行
            statusTable.Controls.Add(new Label { Text = "运行时间:", TextAlign = ContentAlignment.MiddleRight }, 0, 1);
            lblUptime = new Label { Text = "00:00:00", TextAlign = ContentAlignment.MiddleLeft };
            statusTable.Controls.Add(lblUptime, 1, 1);

            statusTable.Controls.Add(new Label { Text = "今日消息:", TextAlign = ContentAlignment.MiddleRight }, 2, 1);
            lblMsgCount = new Label { Text = "0", TextAlign = ContentAlignment.MiddleLeft };
            statusTable.Controls.Add(lblMsgCount, 3, 1);

            // 定时器
            statusTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            statusTimer.Tick += async (s, e) =>
            {
                UpdateUptime();
                await RefreshStatusAsync();
            };
            statusTimer.Start();

            this.ResumeLayout();
        }

        private void BtnExportLog_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtLog.Text))
            {
                MessageBox.Show("日志为空，无需导出。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var sfd = new SaveFileDialog
            {
                Filter = "文本文件|*.txt|所有文件|*.*",
                FileName = $"日志_{DateTime.Now:yyyyMMddHHmmss}.txt"
            };
            if (sfd.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    System.IO.File.WriteAllText(sfd.FileName, txtLog.Text, System.Text.Encoding.UTF8);
                    MessageBox.Show($"导出成功：{sfd.FileName}", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    Logger.Error("导出日志失败", ex);
                    MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private async Task RefreshStatusAsync()
        {
            bool listening = _getListeningStatus();
            lblListenStatus.Text = listening ? "监听中" : "未启动";
            lblListenStatus.ForeColor = listening ? Color.Green : Color.Red;

            bool dbOk = await _testConnectionAsync();
            lblDbStatus.Text = dbOk ? "正常" : "断开";
            lblDbStatus.ForeColor = dbOk ? Color.Green : Color.Red;

            int todayCount = await _getTodayCountAsync();
            lblMsgCount.Text = todayCount >= 0 ? todayCount.ToString() : "Error";
        }

        private void UpdateUptime()
        {
            TimeSpan elapsed = DateTime.Now - startTime;
            lblUptime.Text = elapsed.ToString(@"hh\:mm\:ss");
        }

        private void AppendLogToUI(string logEntry)
        {
            if (txtLog.InvokeRequired)
                txtLog.Invoke(new Action<string>(AppendLogToUI), logEntry);
            else
            {
                txtLog.AppendText(logEntry + "\n");
                txtLog.SelectionStart = txtLog.Text.Length;
                txtLog.ScrollToCaret();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Logger.OnLogWritten -= AppendLogToUI;
                statusTimer?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}