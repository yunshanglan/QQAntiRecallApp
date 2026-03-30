using System;
using System.Drawing;
using System.Windows.Forms;
using System.Diagnostics;

namespace QQAntiRecallApp
{
    public partial class SettingsPageControl : UserControl
    {
        private readonly AppConfig _config;

        public event Action SettingsSaved;

        public event Action RequestDatabaseReset;

        public SettingsPageControl(AppConfig config)
        {
            _config = config;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 6,
                Padding = new Padding(20)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            this.Controls.Add(layout);

            int row = 0;

            // 仅保存QQ消息
            layout.Controls.Add(new Label { Text = "仅保存QQ消息:", TextAlign = ContentAlignment.MiddleRight, Anchor = AnchorStyles.Right }, 0, row);
            CheckBox chkOnlyQQ = new CheckBox { Checked = _config.OnlyQQ, Dock = DockStyle.Fill };
            chkOnlyQQ.CheckedChanged += (s, e) => _config.OnlyQQ = chkOnlyQQ.Checked;
            layout.Controls.Add(chkOnlyQQ, 1, row++);

            // 自动清理天数
            layout.Controls.Add(new Label { Text = "自动清理天数 (0=不清理):", TextAlign = ContentAlignment.MiddleRight, Anchor = AnchorStyles.Right }, 0, row);
            NumericUpDown numClean = new NumericUpDown { Value = _config.AutoCleanDays, Minimum = 0, Maximum = 365, Dock = DockStyle.Fill };
            numClean.ValueChanged += (s, e) => _config.AutoCleanDays = (int)numClean.Value;
            layout.Controls.Add(numClean, 1, row++);

            // 启动时最小化
            layout.Controls.Add(new Label { Text = "启动时最小化:", TextAlign = ContentAlignment.MiddleRight, Anchor = AnchorStyles.Right }, 0, row);
            CheckBox chkStartMinimized = new CheckBox { Checked = _config.StartMinimized, Dock = DockStyle.Fill };
            chkStartMinimized.CheckedChanged += (s, e) => _config.StartMinimized = chkStartMinimized.Checked;
            layout.Controls.Add(chkStartMinimized, 1, row++);

            // 数据库文件信息（只读）
            layout.Controls.Add(new Label { Text = "数据库文件:", TextAlign = ContentAlignment.MiddleRight, Anchor = AnchorStyles.Right }, 0, row);
            TextBox txtDbPath = new TextBox { Text = _config.DbFilePath, ReadOnly = true, Dock = DockStyle.Fill };
            layout.Controls.Add(txtDbPath, 1, row++);

            // 按钮区域
            FlowLayoutPanel buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 5, 0, 0) };

            Button btnSave = new Button { Text = "保存设置", AutoSize = true };
            btnSave.Click += (s, e) => SettingsSaved?.Invoke();
            buttonPanel.Controls.Add(btnSave);

            Button btnOpenDbFolder = new Button { Text = "打开数据库文件夹", AutoSize = true };
            btnOpenDbFolder.Click += (s, e) =>
            {
                string folder = System.IO.Path.GetDirectoryName(_config.DbFilePath);
                if (!string.IsNullOrEmpty(folder) && System.IO.Directory.Exists(folder))
                {
                    Process.Start("explorer.exe", folder);
                }
                else
                {
                    MessageBox.Show("数据库文件夹不存在。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
            buttonPanel.Controls.Add(btnOpenDbFolder);

            // 可选：重置数据库按钮（谨慎）
            Button btnResetDb = new Button { Text = "重置数据库", AutoSize = true, ForeColor = Color.Red };
            btnResetDb.Click += (s, e) =>
            {
                if (MessageBox.Show("重置数据库将删除所有已保存的消息，且不可恢复。确定继续吗？",
                    "警告", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    RequestDatabaseReset?.Invoke();
                }
            };
            buttonPanel.Controls.Add(btnResetDb);

            layout.Controls.Add(buttonPanel, 1, row++);

            // 填充空行
            for (; row < 6; row++)
            {
                layout.Controls.Add(new Label(), 0, row);
                layout.Controls.Add(new Panel(), 1, row);
            }

            this.ResumeLayout();
        }
    }
}