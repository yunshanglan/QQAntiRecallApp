using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Windows.Foundation.Metadata;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using Microsoft.Data.Sqlite;
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
        private string _connectionString;        // SQLite 连接字符串
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
            Logger.Debug("Form1 构造函数开始");
            InitializeComponent();
            this.Load += Form1_Load;
            this.FormClosing += Form1_FormClosing;

            configPath = Path.Combine(Application.StartupPath, "config.json");
            Logger.Debug($"配置文件路径: {configPath}");
            LoadConfig();
            UpdateConnectionString();

            InitializeCustomControls();

            startTime = DateTime.Now;
            Logger.Debug("Form1 构造函数结束");
        }

        private void InitializeCustomControls()
        {
            Logger.Debug("初始化自定义控件");
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
            viewerPage = new ViewerPageControl(_connectionString);
            viewerPage.Dock = DockStyle.Fill;
            viewerPageTab.Controls.Add(viewerPage);
            tabControl.TabPages.Add(viewerPageTab);

            // 第三页：设置
            var settingsPageTab = new TabPage("设置");
            settingsPage = new SettingsPageControl(config);
            settingsPage.Dock = DockStyle.Fill;
            settingsPage.SettingsSaved += OnSettingsSaved;
            settingsPage.RequestDatabaseReset += OnRequestDatabaseReset;
            settingsPageTab.Controls.Add(settingsPage);
            tabControl.TabPages.Add(settingsPageTab);

            // 状态栏
            var statusStrip = new StatusStrip();
            statusLabel = new ToolStripStatusLabel("就绪");
            statusStrip.Items.Add(statusLabel);
            this.Controls.Add(statusStrip);
            Logger.Debug("自定义控件初始化完成");
        }

        private void LoadConfig()
        {
            Logger.Debug("开始加载配置");
            if (File.Exists(configPath))
            {
                try
                {
                    string json = File.ReadAllText(configPath);
                    config = JsonSerializer.Deserialize<AppConfig>(json);
                    Logger.Debug($"配置文件加载成功，内容: {json}");
                }
                catch (Exception ex)
                {
                    Logger.Error("加载配置文件失败", ex);
                    config = new AppConfig();
                }
            }
            else
            {
                Logger.Debug("配置文件不存在，使用默认配置");
                config = new AppConfig();
            }

            // 若 DbFilePath 为空，则设置默认路径
            if (string.IsNullOrEmpty(config.DbFilePath))
            {
                config.DbFilePath = Path.Combine(Application.StartupPath, "messages.db");
                Logger.Debug($"数据库路径设置为默认: {config.DbFilePath}");
            }
            else
            {
                Logger.Debug($"数据库路径使用配置: {config.DbFilePath}");
            }
        }

        private void SaveConfig()
        {
            try
            {
                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configPath, json);
                Logger.Debug($"配置保存成功: {json}");
            }
            catch (Exception ex) { Logger.Error("保存配置失败", ex); }
        }

        private void UpdateConnectionString()
        {
            _connectionString = $"Data Source={config.DbFilePath}";
            Logger.Debug($"连接字符串更新: {_connectionString}");
        }

        /// <summary>
        /// 初始化数据库：确保目录存在、文件存在、表结构正确
        /// </summary>
        private async Task<bool> InitializeDatabaseAsync()
        {
            Logger.Debug($"初始化数据库，路径: {config.DbFilePath}");
            try
            {
                string directory = Path.GetDirectoryName(config.DbFilePath);
                if (!Directory.Exists(directory))
                {
                    Logger.Debug($"创建数据库目录: {directory}");
                    Directory.CreateDirectory(directory);
                }

                bool dbExists = File.Exists(config.DbFilePath);
                Logger.Debug($"数据库文件存在: {dbExists}");
                using (var conn = new SqliteConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    Logger.Debug("数据库连接已打开");

                    if (!dbExists)
                    {
                        // 建表
                        const string createTableSql = @"
                            CREATE TABLE messages (
                                id INTEGER PRIMARY KEY AUTOINCREMENT,
                                group_name TEXT NOT NULL,
                                sender TEXT NOT NULL,
                                message TEXT NOT NULL,
                                created_at DATETIME NOT NULL
                            );
                            CREATE INDEX idx_created_at ON messages(created_at);";
                        using (var cmd = new SqliteCommand(createTableSql, conn))
                        {
                            await cmd.ExecuteNonQueryAsync();
                        }
                        Logger.Info("数据库文件已创建并初始化表结构");
                    }
                    else
                    {
                        // 可选：检查表是否存在，若不存在则创建（兼容旧版本）
                        const string checkTableSql = "SELECT name FROM sqlite_master WHERE type='table' AND name='messages';";
                        using (var cmd = new SqliteCommand(checkTableSql, conn))
                        {
                            var result = await cmd.ExecuteScalarAsync();
                            if (result == null)
                            {
                                Logger.Warning("messages表不存在，正在创建");
                                const string createTableSql = @"
                                    CREATE TABLE messages (
                                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                                        group_name TEXT NOT NULL,
                                        sender TEXT NOT NULL,
                                        message TEXT NOT NULL,
                                        created_at DATETIME NOT NULL
                                    );
                                    CREATE INDEX idx_created_at ON messages(created_at);";
                                using (var createCmd = new SqliteCommand(createTableSql, conn))
                                {
                                    await createCmd.ExecuteNonQueryAsync();
                                }
                                Logger.Info("已添加缺失的消息表");
                            }
                            else
                            {
                                Logger.Debug("messages表已存在");
                            }
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("初始化数据库失败", ex);
                return false;
            }
        }

        private async Task<bool> TestDatabaseConnectionAsync()
        {
            try
            {
                using (var conn = new SqliteConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"数据库连接测试失败: {ex.Message}");
                return false;
            }
        }

        private async Task<int> GetTodayMessageCountAsync()
        {
            try
            {
                using (var conn = new SqliteConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    string sql = "SELECT COUNT(*) FROM messages WHERE DATE(created_at) = DATE('now', 'localtime')";
                    using (var cmd = new SqliteCommand(sql, conn))
                    {
                        int count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                        return count;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"获取今日消息数失败: {ex.Message}");
                return -1;
            }
        }

        /// <summary>
        /// 启动监听（权限请求、事件订阅）
        /// </summary>
        private async Task<bool> StartListeningAsync()
        {
            Logger.Debug("StartListeningAsync 开始");
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
                Logger.Debug("通知侦听器API可用");

                // 确保数据库已初始化
                bool dbReady = await InitializeDatabaseAsync();
                if (!dbReady)
                {
                    Logger.Error("数据库初始化失败，监听启动失败");
                    return false;
                }

                // 获取侦听器实例（单例）
                if (listener == null)
                {
                    listener = UserNotificationListener.Current;
                    Logger.Debug("获取UserNotificationListener实例");
                }

                // 请求权限
                var accessStatus = await listener.RequestAccessAsync();
                Logger.Debug($"通知访问权限状态: {accessStatus}");
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
            Logger.Info("=== 程序启动，开始初始化 ===");

            // 1. 输出当前工作目录和程序路径
            string currentDir = Directory.GetCurrentDirectory();
            string appPath = Application.StartupPath;
            Logger.Info($"当前工作目录: {currentDir}");
            Logger.Info($"程序启动目录: {appPath}");

            // 2. 输出配置文件信息
            string configFullPath = Path.Combine(Application.StartupPath, "config.json");
            Logger.Info($"配置文件路径: {configFullPath}");
            Logger.Info($"配置文件是否存在: {File.Exists(configFullPath)}");

            // 3. 输出配置中的数据库文件路径（加载配置后）
            LoadConfig();
            Logger.Info($"配置中的数据库文件路径: {config.DbFilePath}");
            string dbDir = Path.GetDirectoryName(config.DbFilePath);
            Logger.Info($"数据库目录: {dbDir}");
            Logger.Info($"数据库目录是否存在: {Directory.Exists(dbDir)}");

            // 尝试创建目录并测试写入权限
            if (!Directory.Exists(dbDir))
            {
                Logger.Info($"数据库目录不存在，尝试创建...");
                try
                {
                    Directory.CreateDirectory(dbDir);
                    Logger.Info($"目录创建成功，权限测试：创建临时文件...");
                    string testFile = Path.Combine(dbDir, "test_write.tmp");
                    File.WriteAllText(testFile, "test");
                    File.Delete(testFile);
                    Logger.Info($"目录可写，权限正常。");
                }
                catch (Exception ex)
                {
                    Logger.Error($"创建目录或写入测试失败: {ex.Message}");
                }
            }
            else
            {
                // 测试现有目录写入权限
                try
                {
                    string testFile = Path.Combine(dbDir, "test_write.tmp");
                    File.WriteAllText(testFile, "test");
                    File.Delete(testFile);
                    Logger.Info($"数据库目录可写，权限正常。");
                }
                catch (Exception ex)
                {
                    Logger.Error($"数据库目录不可写: {ex.Message}");
                }
            }

            // 4. 输出数据库文件是否存在
            Logger.Info($"数据库文件是否存在: {File.Exists(config.DbFilePath)}");

            // 5. API 可用性检查
            if (!ApiInformation.IsTypePresent("Windows.UI.Notifications.Management.UserNotificationListener"))
            {
                string msg = "当前Windows版本不支持通知侦听器。需要Windows 10 周年更新（Build 14393）或更高版本。";
                Logger.Error(msg);
                MessageBox.Show(msg, "不支持的API", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit();
                return;
            }
            Logger.Info("API 检查通过");

            // 6. 系统版本和 AppInfo 可用性
            var osVersion = Environment.OSVersion.Version;
            bool isAppInfoAvailable = ApiInformation.IsPropertyPresent("Windows.UI.Notifications.UserNotification", "AppInfo");
            Logger.Info($"系统版本: {osVersion}, AppInfo 可用: {isAppInfoAvailable}");

            // 7. 包标识检查
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

            // 8. 初始化数据库
            Logger.Info("开始初始化数据库...");
            bool dbInit = await InitializeDatabaseAsync();
            if (!dbInit)
            {
                MessageBox.Show("数据库初始化失败，程序可能无法正常工作。请检查磁盘空间和权限。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                // 仍继续运行，但用户可尝试手动解决
            }
            else
            {
                Logger.Info("数据库初始化成功。");
            }

            // 9. 尝试启动监听
            Logger.Info("准备启动监听...");
            bool started = await StartListeningAsync();
            if (started)
            {
                // 根据配置决定是否最小化
                if (config.StartMinimized)
                {
                    this.WindowState = FormWindowState.Minimized;
                    this.ShowInTaskbar = false;
                    Logger.Info("已按设置最小化到托盘");
                }
                else
                {
                    this.WindowState = FormWindowState.Normal;
                    this.ShowInTaskbar = true;
                    Logger.Info("窗口正常显示");
                }
            }
            else
            {
                // 启动监听失败，显示窗口
                this.WindowState = FormWindowState.Normal;
                this.ShowInTaskbar = true;
                Logger.Info("监听启动失败，窗口正常显示");
            }

            // 确保托盘图标已创建
            if (trayIcon == null)
                CreateTrayIcon();

            Logger.Info("=== 程序初始化完成 ===");
        }

        private async void OnSettingsSaved()
        {
            Logger.Info("用户保存设置");
            SaveConfig();
            UpdateConnectionString();
            viewerPage.UpdateConnectionString(_connectionString);
            Logger.Info("配置已保存");

            // 如果监听未启动，尝试启动；如果已启动，重新初始化数据库连接
            if (!_isListeningStarted)
            {
                await StartListeningAsync();
            }
            else
            {
                // 仅刷新数据库连接（实际上SQLite连接字符串变化需重新连接）
                // 但通常数据库路径不会在运行中改变，此处仅确保连接字符串更新
                Logger.Info("配置已更新，监听继续运行");
            }
        }

        private async void OnRequestDatabaseReset()
        {
            Logger.Warning("用户请求重置数据库（清空消息表）");

            // 停止监听，防止清空过程中有新消息写入
            StopListening();

            // 等待可能正在进行的数据库操作完成（例如正在保存通知）
            Logger.Debug("等待2秒以确保所有数据库操作完成...");
            await Task.Delay(2000);

            // 强制垃圾回收，释放可能残留的SQLite连接句柄
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Logger.Debug("垃圾回收完成");

            try
            {
                // 检查数据库文件是否存在
                if (!File.Exists(config.DbFilePath))
                {
                    Logger.Warning("数据库文件不存在，无需清空");
                    MessageBox.Show("数据库文件不存在，无需重置。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // 打开数据库连接，清空 messages 表并重置自增序列
                using (var conn = new SqliteConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var transaction = conn.BeginTransaction())
                    {
                        // 删除所有消息记录
                        string deleteSql = "DELETE FROM messages;";
                        using (var cmd = new SqliteCommand(deleteSql, conn, transaction))
                        {
                            int rows = await cmd.ExecuteNonQueryAsync();
                            Logger.Info($"已删除 {rows} 条消息记录");
                        }

                        // 重置自增ID序列（可选）
                        string resetSequenceSql = "DELETE FROM sqlite_sequence WHERE name='messages';";
                        using (var cmd = new SqliteCommand(resetSequenceSql, conn, transaction))
                        {
                            await cmd.ExecuteNonQueryAsync();
                            Logger.Debug("已重置 messages 表的自增序列");
                        }

                        transaction.Commit();
                        Logger.Info("数据库消息表已清空");
                    }
                }

                // 清空成功后，刷新消息查看器页面
                viewerPage?.RefreshDataAsync(); // 需要添加此方法（见下方补充）

                MessageBox.Show("消息记录已全部清空。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Logger.Error("清空消息表失败", ex);
                MessageBox.Show($"清空失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                // 无论清空是否成功，都尝试重新启动监听
                bool started = await StartListeningAsync();
                if (!started)
                {
                    Logger.Error("清空后重启监听失败");
                    MessageBox.Show("监听服务未能重新启动，请检查设置或手动重启应用。", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void Listener_NotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
        {
            if (args.ChangeKind == UserNotificationChangedKind.Added)
            {
                Logger.Debug($"收到新通知，ID: {args.UserNotificationId}");
                _ = Task.Run(() => HandleNewNotification(args.UserNotificationId));
            }
            else
            {
                Logger.Debug($"通知变更类型: {args.ChangeKind}，ID: {args.UserNotificationId}");
            }
        }

        private async void HandleNewNotification(uint notificationId)
        {
            Logger.Debug($"开始处理通知 ID: {notificationId}");
            if (!_processingIds.TryAdd(notificationId, true))
            {
                Logger.Warning($"通知 ID {notificationId} 重复，忽略");
                return;
            }
            try
            {
                var notification = listener.GetNotification(notificationId);
                if (notification != null)
                {
                    Logger.Debug($"获取到通知对象，应用名: {notification.AppInfo?.DisplayInfo?.DisplayName}");
                    await SaveNotificationToDatabaseAsync(notification);
                }
                else
                {
                    Logger.Warning($"通知 ID {notificationId} 获取为 null");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"处理通知时出错", ex);
            }
            finally
            {
                _processingIds.TryRemove(notificationId, out _);
                Logger.Debug($"通知 ID {notificationId} 处理完成");
            }
        }

        private async Task SaveNotificationToDatabaseAsync(UserNotification notification)
        {
            Logger.Debug("开始保存通知到数据库");
            try
            {
                // 过滤非QQ消息
                if (config.OnlyQQ)
                {
                    string appName = null;
                    try { appName = notification.AppInfo?.DisplayInfo?.DisplayName; } catch { }
                    Logger.Debug($"通知来源应用名: {appName}");
                    if (appName != null && !appName.Contains("QQ", StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.Info($"非QQ通知已忽略，应用名: {appName}");
                        return;
                    }
                }

                DateTime creationTime = notification.CreationTime.DateTime.ToUniversalTime();
                Logger.Debug($"通知创建时间(UTC): {creationTime}");

                var binding = notification.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
                if (binding == null)
                {
                    Logger.Warning("通知无视觉绑定");
                    return;
                }

                var textElements = binding.GetTextElements();
                if (textElements.Count == 0)
                {
                    Logger.Warning("通知无文本元素");
                    return;
                }

                string title = textElements[0].Text ?? "";
                string content = textElements.Count > 1 ? string.Join("\n", textElements.Skip(1).Select(t => t.Text ?? "")) : "";
                Logger.Debug($"原始通知内容: Title={title}, Content={content}");

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
                Logger.Debug($"解析后: 群组={groupName}, 发送者={sender}, 消息={message}");

                const string insertSql = @"
                    INSERT INTO messages (group_name, sender, message, created_at)
                    VALUES (@groupName, @sender, @message, @createdAt);";
                using (var conn = new SqliteConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new SqliteCommand(insertSql, conn))
                    {
                        cmd.Parameters.AddWithValue("@groupName", groupName);
                        cmd.Parameters.AddWithValue("@sender", sender);
                        cmd.Parameters.AddWithValue("@message", message);
                        cmd.Parameters.AddWithValue("@createdAt", creationTime);
                        int rows = await cmd.ExecuteNonQueryAsync();
                        Logger.Debug($"插入数据库，影响行数: {rows}");
                    }
                }
                Logger.Info($"保存成功: 群组={groupName}, 发送者={sender}");
            }
            catch (Exception ex)
            {
                Logger.Error("保存到数据库失败", ex);
            }
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
            string cleaned = content.Substring(index).TrimStart();
            Logger.Debug($"内容清洗: '{content}' -> '{cleaned}'");
            return cleaned;
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
            Logger.Debug("托盘图标已创建");
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            Logger.Info("程序关闭");
            StopListening();
            logPage?.Dispose();
            trayIcon?.Dispose();
            Logger.Debug("资源释放完成");
        }
    }
}