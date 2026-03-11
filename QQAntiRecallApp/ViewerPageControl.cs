using System;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MySql.Data.MySqlClient;

namespace QQAntiRecallApp
{
    public partial class ViewerPageControl : UserControl
    {
        private DataGridView dgvMessages;
        private ComboBox cmbGroupFilter;
        private DateTimePicker dtpStartDate, dtpEndDate;
        private Button btnRefresh, btnExportCsv;
        private Label lblTotalCount;

        private string _connectionString;

        public ViewerPageControl(string connectionString)
        {
            _connectionString = connectionString;
            InitializeComponent();
            _ = LoadGroupsAsync(); // 异步加载群组列表
        }

        public void UpdateConnectionString(string newConnectionString)
        {
            _connectionString = newConnectionString;
            // 可重新加载群组，但简单起见，可让用户手动刷新
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            // 使用 TableLayoutPanel 布局
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            this.Controls.Add(layout);

            // 筛选面板
            Panel filterPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
            layout.Controls.Add(filterPanel, 0, 0);

            // 群组
            filterPanel.Controls.Add(new Label { Text = "群组:", Location = new Point(10, 15), Size = new Size(40, 25) });
            cmbGroupFilter = new ComboBox { Location = new Point(55, 12), Size = new Size(150, 25), DropDownStyle = ComboBoxStyle.DropDownList };
            cmbGroupFilter.Items.Insert(0, "全部");
            cmbGroupFilter.SelectedIndex = 0;
            filterPanel.Controls.Add(cmbGroupFilter);

            // 开始日期
            filterPanel.Controls.Add(new Label { Text = "从:", Location = new Point(220, 15), Size = new Size(25, 25) });
            dtpStartDate = new DateTimePicker { Location = new Point(250, 12), Size = new Size(130, 25), Format = DateTimePickerFormat.Short, Value = DateTime.Today.AddDays(-7) };
            filterPanel.Controls.Add(dtpStartDate);

            // 结束日期
            filterPanel.Controls.Add(new Label { Text = "到:", Location = new Point(395, 15), Size = new Size(25, 25) });
            dtpEndDate = new DateTimePicker { Location = new Point(425, 12), Size = new Size(130, 25), Format = DateTimePickerFormat.Short, Value = DateTime.Today.AddDays(1) };
            filterPanel.Controls.Add(dtpEndDate);

            // 按钮
            btnRefresh = new Button { Text = "刷新", Location = new Point(570, 10), Size = new Size(80, 30) };
            btnRefresh.Click += async (s, e) => await LoadMessagesAsync();
            filterPanel.Controls.Add(btnRefresh);

            btnExportCsv = new Button { Text = "导出CSV", Location = new Point(660, 10), Size = new Size(80, 30) };
            btnExportCsv.Click += BtnExportCsv_Click;
            filterPanel.Controls.Add(btnExportCsv);

            lblTotalCount = new Label { Text = "共 0 条记录", Location = new Point(760, 15), Size = new Size(150, 25), TextAlign = ContentAlignment.MiddleLeft };
            filterPanel.Controls.Add(lblTotalCount);

            // DataGridView
            dgvMessages = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = true
            };
            layout.Controls.Add(dgvMessages, 0, 1);

            this.ResumeLayout();
        }

        private async Task LoadGroupsAsync()
        {
            try
            {
                using (var conn = new MySqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    string sql = "SELECT DISTINCT group_name FROM messages ORDER BY group_name";
                    using (var cmd = new MySqlCommand(sql, conn))
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                            cmbGroupFilter.Items.Add(reader.GetString(0));
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("加载群组列表失败", ex);
            }
        }

        private async Task LoadMessagesAsync()
        {
            try
            {
                string groupFilter = cmbGroupFilter.SelectedItem?.ToString();
                DateTime start = dtpStartDate.Value.Date;
                DateTime end = dtpEndDate.Value.Date.AddDays(1).AddSeconds(-1);

                string sql = @"SELECT id, group_name, sender, message, created_at 
                               FROM messages 
                               WHERE created_at BETWEEN @start AND @end";
                if (!string.IsNullOrEmpty(groupFilter) && groupFilter != "全部")
                    sql += " AND group_name = @group";
                sql += " ORDER BY created_at DESC";

                DataTable dt = new DataTable();
                using (var conn = new MySqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new MySqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@start", start);
                        cmd.Parameters.AddWithValue("@end", end);
                        if (!string.IsNullOrEmpty(groupFilter) && groupFilter != "全部")
                            cmd.Parameters.AddWithValue("@group", groupFilter);

                        using (var adapter = new MySqlDataAdapter(cmd))
                            adapter.Fill(dt);
                    }
                }

                dgvMessages.DataSource = dt;
                if (dt.Columns.Count > 0)
                {
                    dgvMessages.Columns["id"].HeaderText = "ID";
                    dgvMessages.Columns["group_name"].HeaderText = "群组";
                    dgvMessages.Columns["sender"].HeaderText = "发送者";
                    dgvMessages.Columns["message"].HeaderText = "内容";
                    dgvMessages.Columns["created_at"].HeaderText = "时间";
                }
                lblTotalCount.Text = $"共 {dt.Rows.Count} 条记录";
            }
            catch (Exception ex)
            {
                Logger.Error("加载消息失败", ex);
                MessageBox.Show($"加载消息失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void BtnExportCsv_Click(object sender, EventArgs e)
        {
            if (dgvMessages.Rows.Count == 0)
            {
                MessageBox.Show("没有数据可导出", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var sfd = new SaveFileDialog
            {
                Filter = "CSV文件|*.csv",
                FileName = $"消息导出_{DateTime.Now:yyyyMMddHHmmss}.csv"
            };
            if (sfd.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    using (var writer = new System.IO.StreamWriter(sfd.FileName, false, System.Text.Encoding.UTF8))
                    {
                        var columnNames = dgvMessages.Columns.Cast<DataGridViewColumn>().Select(c => c.HeaderText).ToArray();
                        writer.WriteLine(string.Join(",", columnNames.Select(c => $"\"{c}\"")));
                        foreach (DataGridViewRow row in dgvMessages.Rows)
                        {
                            var cells = row.Cells.Cast<DataGridViewCell>()
                                .Select(c => c.Value?.ToString()?.Replace("\"", "\"\"") ?? "");
                            writer.WriteLine(string.Join(",", cells.Select(c => $"\"{c}\"")));
                        }
                    }
                    MessageBox.Show($"导出成功：{sfd.FileName}", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    Logger.Error("导出CSV失败", ex);
                    MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}