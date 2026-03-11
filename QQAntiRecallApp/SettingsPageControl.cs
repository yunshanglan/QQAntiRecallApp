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
        public event Action OpenConfigFileRequested;

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
                RowCount = 8,
                Padding = new Padding(20)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            this.Controls.Add(layout);

            // 行1：服务器
            layout.Controls.Add(new Label { Text = "数据库服务器:", TextAlign = ContentAlignment.MiddleRight, Anchor = AnchorStyles.Right }, 0, 0);
            TextBox txtServer = new TextBox { Text = _config.DbServer, Dock = DockStyle.Fill };
            txtServer.TextChanged += (s, e) => _config.DbServer = txtServer.Text;
            layout.Controls.Add(txtServer, 1, 0);

            // 行2：用户名
            layout.Controls.Add(new Label { Text = "用户名:", TextAlign = ContentAlignment.MiddleRight, Anchor = AnchorStyles.Right }, 0, 1);
            TextBox txtUser = new TextBox { Text = _config.DbUser, Dock = DockStyle.Fill };
            txtUser.TextChanged += (s, e) => _config.DbUser = txtUser.Text;
            layout.Controls.Add(txtUser, 1, 1);

            // 行3：密码
            layout.Controls.Add(new Label { Text = "密码:", TextAlign = ContentAlignment.MiddleRight, Anchor = AnchorStyles.Right }, 0, 2);
            TextBox txtPassword = new TextBox { Text = _config.DbPassword, Dock = DockStyle.Fill, PasswordChar = '*' };
            txtPassword.TextChanged += (s, e) => _config.DbPassword = txtPassword.Text;
            layout.Controls.Add(txtPassword, 1, 2);

            // 行4：数据库名
            layout.Controls.Add(new Label { Text = "数据库名:", TextAlign = ContentAlignment.MiddleRight, Anchor = AnchorStyles.Right }, 0, 3);
            TextBox txtDatabase = new TextBox { Text = _config.DbName, Dock = DockStyle.Fill };
            txtDatabase.TextChanged += (s, e) => _config.DbName = txtDatabase.Text;
            layout.Controls.Add(txtDatabase, 1, 3);

            // 行5：仅保存QQ消息
            layout.Controls.Add(new Label { Text = "仅保存QQ消息:", TextAlign = ContentAlignment.MiddleRight, Anchor = AnchorStyles.Right }, 0, 4);
            CheckBox chkOnlyQQ = new CheckBox { Checked = _config.OnlyQQ, Dock = DockStyle.Fill };
            chkOnlyQQ.CheckedChanged += (s, e) => _config.OnlyQQ = chkOnlyQQ.Checked;
            layout.Controls.Add(chkOnlyQQ, 1, 4);

            // 行6：自动清理天数
            layout.Controls.Add(new Label { Text = "自动清理天数 (0=不清理):", TextAlign = ContentAlignment.MiddleRight, Anchor = AnchorStyles.Right }, 0, 5);
            NumericUpDown numClean = new NumericUpDown { Value = _config.AutoCleanDays, Minimum = 0, Maximum = 365, Dock = DockStyle.Fill };
            numClean.ValueChanged += (s, e) => _config.AutoCleanDays = (int)numClean.Value;
            layout.Controls.Add(numClean, 1, 5);

            // 行7：启动时最小化
            layout.Controls.Add(new Label { Text = "启动时最小化:", TextAlign = ContentAlignment.MiddleRight, Anchor = AnchorStyles.Right }, 0, 6);
            CheckBox chkStartMinimized = new CheckBox { Checked = _config.StartMinimized, Dock = DockStyle.Fill };
            chkStartMinimized.CheckedChanged += (s, e) => _config.StartMinimized = chkStartMinimized.Checked;
            layout.Controls.Add(chkStartMinimized, 1, 6);

            // 行8：按钮区域
            FlowLayoutPanel buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };

            Button btnSave = new Button { Text = "保存设置", AutoSize = true };
            btnSave.Click += (s, e) => SettingsSaved?.Invoke();
            buttonPanel.Controls.Add(btnSave);

            Button btnTestConnection = new Button { Text = "测试连接", AutoSize = true };
            btnTestConnection.Click += async (s, e) =>
            {
                using (var conn = new MySql.Data.MySqlClient.MySqlConnection(
                    $"Server={_config.DbServer};Database={_config.DbName};User ID={_config.DbUser};Password={_config.DbPassword};Charset=utf8mb4;"))
                {
                    try
                    {
                        await conn.OpenAsync();
                        MessageBox.Show("连接成功", "测试结果", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"连接失败：{ex.Message}", "测试结果", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            };
            buttonPanel.Controls.Add(btnTestConnection);

            // 新增打开配置文件按钮
            Button btnOpenConfig = new Button { Text = "打开配置文件", AutoSize = true };
            btnOpenConfig.Click += (s, e) => OpenConfigFileRequested?.Invoke();
            buttonPanel.Controls.Add(btnOpenConfig);

            layout.Controls.Add(buttonPanel, 1, 7);

            this.ResumeLayout();
        }
    }
}