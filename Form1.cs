using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Windows.Foundation.Metadata;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using MySql.Data.MySqlClient;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;

namespace QQAntiRecallApp
{
    public partial class Form1 : Form
    {
        private UserNotificationListener listener;
        private NotifyIcon trayIcon;
        private string connectionString;
        private readonly ConcurrentDictionary<uint, bool> _processingIds = new ConcurrentDictionary<uint, bool>();
        private AppConfig config;
        private string configPath;

        private TabControl tabControl;
        private LogPageControl logPage;
        private ViewerPageControl viewerPage;
        private SettingsPageControl settingsPage;
        private DateTime startTime;
        private ToolStripStatusLabel statusLabel;

        private bool _isListeningStarted = false; // 标记是否已启动监听

        public Form1()
        {
            InitializeComponent();
            this.Load += Form1_Load;
            this.FormClosing += Form1_FormClosing;

            configPath = Path.Combine(Application.StartupPath, "config.json");
            LoadConfig();
            UpdateConnectionString();

            InitializeCustomControls();

            startTime = DateTime.Now;
        }

        private void InitializeCustomControls()
        {
            this.Text = "QQ防撤回监听器";
            this.Size = new Size(1000, 700);
            this.StartPosition = FormStartPosition.CenterScreen;

            tabControl = new TabControl { Dock = DockStyle.Fill };
            this.Controls.Add(tabControl);

            // 第一页：日志与状态
            var logPageTab = new TabPage("日志与状态");
            logPage = new LogPageControl(
                testConnectionAsync: TestDatabaseConnectionAsync,
                getTodayCountAsync: GetTodayMessageCountAsync,
                getListeningStatus: () => _isListeningStarted
            );
            logPage.Dock = DockStyle.Fill;
            logPageTab.Controls.Add(logPage);
            tabControl.TabPages.Add(logPageTab);

            // 第二页：消息查看器
            var viewerPageTab = new TabPage("消息查看器");
            viewerPage = new ViewerPageControl(connectionString);
            viewerPage.Dock = DockStyle.Fill;
            viewerPageTab.Controls.Add(viewerPage);
            tabControl.TabPages.Add(viewerPageTab);

            // 第三页：设置
            var settingsPageTab = new TabPage("设置");
            settingsPage = new SettingsPageControl(config);
            settingsPage.Dock = DockStyle.Fill;
            settingsPage.SettingsSaved += OnSettingsSaved;
            settingsPageTab.Controls.Add(settingsPage);
            tabControl.TabPages.Add(settingsPageTab);

            // 状态栏
            var statusStrip = new StatusStrip();
            statusLabel = new ToolStripStatusLabel("就绪");
            statusStrip.Items.Add(statusLabel);
            this.Controls.Add(statusStrip);
        }

        private void LoadConfig()
        {
            if (File.Exists(configPath))
            {
                try
                {
                    string json = File.ReadAllText(configPath);
                    config = JsonSerializer.Deserialize<AppConfig>(json);
                }
                catch { config = new AppConfig(); }
            }
            else config = new AppConfig();
        }

        private void SaveConfig()
        {
            try
            {
                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configPath, json);
            }
            catch (Exception ex) { Logger.Error("保存配置失败", ex); }
        }

        private void UpdateConnectionString()
        {
            connectionString = $"Server={config.DbServer};Database={config.DbName};User ID={config.DbUser};Password={config.DbPassword};Charset=utf8mb4;";
        }

        private bool IsConfigValid()
        {
            return !string.IsNullOrWhiteSpace(config.DbServer) &&
                   !string.IsNullOrWhiteSpace(config.DbUser) &&
                   !string.IsNullOrWhiteSpace(config.DbPassword) &&
                   !string.IsNullOrWhiteSpace(config.DbName);
        }

        private async Task<bool> TestDatabaseConnectionAsync()
        {
            try { using (var conn = new MySqlConnection(connectionString)) { await conn.OpenAsync(); return true; } }
            catch { return false; }
        }

        private async Task<int> GetTodayMessageCountAsync()
        {
            try
            {
                using (var conn = new MySqlConnection(connectionString))
                {
                    await conn.OpenAsync();
                    string sql = "SELECT COUNT(*) FROM messages WHERE DATE(created_at) = CURDATE()";
                    using (var cmd = new MySqlCommand(sql, conn))
                        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
                }
            }
            catch { return -1; }
        }

        /// <summary>
        /// 启动监听（权限请求、事件订阅）
        /// </summary>
        private async Task<bool> StartListeningAsync()
        {
            if (_isListeningStarted)
            {
                Logger.Warning("监听已在运行，先停止");
                StopListening();
            }

            try
            {
                if (!ApiInformation.IsTypePresent("Windows.UI.Notifications.Management.UserNotificationListener"))
                {
                    Logger.Error("当前系统不支持通知侦听器");
                    return false;
                }

                // 确保数据库存在
                bool dbExists = await EnsureDatabaseExistsAsync();
                if (!dbExists)
                {
                    Logger.Error("无法创建或连接数据库，监听启动失败");
                    return false;
                }

                // 确保数据库表存在
                await EnsureDatabaseTableAsync();

                // 获取侦听器实例（单例）
                if (listener == null)
                    listener = UserNotificationListener.Current;

                // 请求权限
                var accessStatus = await listener.RequestAccessAsync();
                if (accessStatus != UserNotificationListenerAccessStatus.Allowed)
                {
                    string msg = accessStatus == UserNotificationListenerAccessStatus.Denied
                        ? "用户拒绝了通知访问权限。请前往系统设置 -> 隐私和安全性 -> 通知访问，手动允许此应用。"
                        : "未获得通知访问权限。";
                    Logger.Error(msg);
                    MessageBox.Show(msg, "权限错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                // 订阅事件
                listener.NotificationChanged += Listener_NotificationChanged;
                _isListeningStarted = true;
                Logger.Info("监听已启动");
                statusLabel.Text = "运行中";
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("启动监听失败", ex);
                return false;
            }
        }

        /// <summary>
        /// 停止监听（取消事件订阅）
        /// </summary>
        private void StopListening()
        {
            if (listener != null && _isListeningStarted)
            {
                listener.NotificationChanged -= Listener_NotificationChanged;
                _isListeningStarted = false;
                Logger.Info("监听已停止");
            }
        }

        private async void Form1_Load(object sender, EventArgs e)
        {
            Logger.Info("程序开始加载...");

            // 1. API 可用性检查
            if (!ApiInformation.IsTypePresent("Windows.UI.Notifications.Management.UserNotificationListener"))
            {
                string msg = "当前Windows版本不支持通知侦听器。需要Windows 10 周年更新（Build 14393）或更高版本。";
                Logger.Error(msg);
                MessageBox.Show(msg, "不支持的API", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit();
                return;
            }
            Logger.Info("API 检查通过");

            // 2. 系统版本和 AppInfo 可用性（可选）
            var osVersion = Environment.OSVersion.Version;
            bool isAppInfoAvailable = ApiInformation.IsPropertyPresent("Windows.UI.Notifications.UserNotification", "AppInfo");
            Logger.Info($"系统版本: {osVersion}, AppInfo 可用: {isAppInfoAvailable}");

            // 3. 包标识检查
            bool hasPackageIdentity = false;
            string packageFamilyName = "";
            try
            {
                var currentPackage = Windows.ApplicationModel.Package.Current;
                packageFamilyName = currentPackage.Id.FamilyName;
                hasPackageIdentity = true;
            }
            catch (InvalidOperationException)
            {
                hasPackageIdentity = false;
            }
            Logger.Info($"具有包标识: {hasPackageIdentity}，包家族名: {packageFamilyName}");

            // 4. 检查配置是否有效
            if (!IsConfigValid())
            {
                // 配置无效，显示主窗口并提示用户设置
                this.WindowState = FormWindowState.Normal;
                this.ShowInTaskbar = true;
                MessageBox.Show("数据库配置不完整，请前往“设置”页面填写数据库信息并保存。", "首次运行", MessageBoxButtons.OK, MessageBoxIcon.Information);
                // 创建托盘图标（但不启动监听）
                CreateTrayIcon();
                return;
            }

            // 5. 配置有效，尝试启动监听
            bool started = await StartListeningAsync();
            if (started)
            {
                // 根据配置决定是否最小化
                if (config.StartMinimized)
                {
                    this.WindowState = FormWindowState.Minimized;
                    this.ShowInTaskbar = false;
                }
                else
                {
                    this.WindowState = FormWindowState.Normal;
                    this.ShowInTaskbar = true;
                }
            }
            else
            {
                // 启动监听失败，显示窗口
                this.WindowState = FormWindowState.Normal;
                this.ShowInTaskbar = true;
            }

            // 确保托盘图标已创建（若未创建则创建）
            if (trayIcon == null)
                CreateTrayIcon();
        }

        private async void OnSettingsSaved()
        {
            SaveConfig();
            UpdateConnectionString();
            viewerPage.UpdateConnectionString(connectionString);
            Logger.Info("配置已保存");

            // 如果配置有效，尝试启动监听
            if (IsConfigValid())
            {
                await StartListeningAsync();
            }
            else
            {
                MessageBox.Show("配置不完整，请填写所有数据库字段。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// 确保数据库存在，如果不存在则创建
        /// </summary>
        private async Task<bool> EnsureDatabaseExistsAsync()
        {
            // 构建不包含数据库名的连接字符串（只连接到服务器）
            string serverConnectionString = $"Server={config.DbServer};User ID={config.DbUser};Password={config.DbPassword};Charset=utf8mb4;";
            try
            {
                using (var conn = new MySqlConnection(serverConnectionString))
                {
                    await conn.OpenAsync();
                    string createDbSql = $"CREATE DATABASE IF NOT EXISTS `{config.DbName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
                    using (var cmd = new MySqlCommand(createDbSql, conn))
                    {
                        await cmd.ExecuteNonQueryAsync();
                    }
                    Logger.Info($"数据库 {config.DbName} 已确保存在");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"创建数据库失败: {ex.Message}");
                return false;
            }
        }

        private async Task EnsureDatabaseTableAsync()
        {
            const string sql = @"
                CREATE TABLE IF NOT EXISTS messages (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    group_name VARCHAR(255) NOT NULL,
                    sender VARCHAR(255) NOT NULL,
                    message TEXT NOT NULL,
                    created_at DATETIME NOT NULL,
                    INDEX idx_created_at (created_at)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;";
            using (var conn = new MySqlConnection(connectionString))
            {
                await conn.OpenAsync();
                using (var cmd = new MySqlCommand(sql, conn))
                    await cmd.ExecuteNonQueryAsync();
            }
        }

        private void Listener_NotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
        {
            if (args.ChangeKind == UserNotificationChangedKind.Added)
            {
                Logger.Info($"收到新通知，ID: {args.UserNotificationId}");
                _ = Task.Run(() => HandleNewNotification(args.UserNotificationId));
            }
        }

        private async void HandleNewNotification(uint notificationId)
        {
            if (!_processingIds.TryAdd(notificationId, true))
            {
                Logger.Warning($"通知 ID {notificationId} 重复，忽略");
                return;
            }
            try
            {
                var notification = listener.GetNotification(notificationId);
                if (notification != null) await SaveNotificationToDatabaseAsync(notification);
                else Logger.Warning($"通知 ID {notificationId} 获取为 null");
            }
            catch (Exception ex) { Logger.Error($"处理通知时出错", ex); }
        }

        private async Task SaveNotificationToDatabaseAsync(UserNotification notification)
        {
            try
            {
                // 过滤非QQ消息
                if (config.OnlyQQ)
                {
                    string appName = null;
                    try { appName = notification.AppInfo?.DisplayInfo?.DisplayName; } catch { }
                    if (appName != null && !appName.Contains("QQ", StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.Info($"非QQ通知已忽略，应用名: {appName}");
                        return;
                    }
                }

                DateTime creationTime = notification.CreationTime.DateTime.ToUniversalTime();
                var binding = notification.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
                if (binding == null) { Logger.Warning("通知无视觉绑定"); return; }
                var textElements = binding.GetTextElements();
                if (textElements.Count == 0) { Logger.Warning("通知无文本元素"); return; }

                string title = textElements[0].Text ?? "";
                string content = textElements.Count > 1 ? string.Join("\n", textElements.Skip(1).Select(t => t.Text ?? "")) : "";

                string groupName = title;
                string sender, message;
                int colonIndex = content.IndexOf('：');
                if (colonIndex == -1) colonIndex = content.IndexOf(':');
                if (colonIndex > 0)
                {
                    sender = CleanContent(content.Substring(0, colonIndex).Trim());
                    message = content.Substring(colonIndex + 1).Trim();
                }
                else
                {
                    sender = title;
                    message = content;
                }

                const string insertSql = @"
                    INSERT INTO messages (group_name, sender, message, created_at)
                    VALUES (@groupName, @sender, @message, @createdAt);";
                using (var conn = new MySqlConnection(connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new MySqlCommand(insertSql, conn))
                    {
                        cmd.Parameters.AddWithValue("@groupName", groupName);
                        cmd.Parameters.AddWithValue("@sender", sender);
                        cmd.Parameters.AddWithValue("@message", message);
                        cmd.Parameters.AddWithValue("@createdAt", creationTime);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
                Logger.Info($"保存成功: 群组={groupName}, 发送者={sender}");
            }
            catch (Exception ex) { Logger.Error("保存到数据库失败", ex); }
        }

        private string CleanContent(string content)
        {
            if (string.IsNullOrEmpty(content)) return content;
            int index = 0;
            while (index < content.Length && content[index] == '[')
            {
                int close = content.IndexOf(']', index);
                if (close == -1) break;
                index = close + 1;
                while (index < content.Length && content[index] == ' ') index++;
            }
            return content.Substring(index).TrimStart();
        }

        private void CreateTrayIcon()
        {
            if (trayIcon != null) return; // 防止重复创建
            trayIcon = new NotifyIcon
            {
                Icon = System.Drawing.SystemIcons.Application,
                Text = "QQ防撤回监听器",
                Visible = true
            };
            var menu = new ContextMenuStrip();
            menu.Items.Add("显示窗口", null, (s, e) => { this.WindowState = FormWindowState.Normal; this.ShowInTaskbar = true; this.Activate(); });
            menu.Items.Add("退出", null, (s, e) => Application.Exit());
            trayIcon.ContextMenuStrip = menu;
            trayIcon.DoubleClick += (s, e) => { this.WindowState = FormWindowState.Normal; this.ShowInTaskbar = true; this.Activate(); };
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            StopListening();
            logPage?.Dispose();
            trayIcon?.Dispose();
            Logger.Info("程序关闭");
        }
    }
}