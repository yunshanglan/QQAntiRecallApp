using System.Text.Json.Serialization;

namespace QQAntiRecallApp
{
    /// <summary>
    /// 应用程序配置类，存储用户可调整的设置项。
    /// </summary>
    public class AppConfig
    {
        /// <summary>
        /// 数据库服务器地址
        /// </summary>
        public string DbServer { get; set; } = "";

        /// <summary>
        /// 数据库用户名
        /// </summary>
        public string DbUser { get; set; } = "";

        /// <summary>
        /// 数据库密码
        /// </summary>
        public string DbPassword { get; set; } = "";

        /// <summary>
        /// 数据库名
        /// </summary>
        public string DbName { get; set; } = "qqantirecall";

        /// <summary>
        /// 是否仅保存QQ消息（过滤其他应用）
        /// </summary>
        public bool OnlyQQ { get; set; } = true;

        /// <summary>
        /// 自动清理早于此天数的消息（0表示不清理）
        /// </summary>
        public int AutoCleanDays { get; set; } = 0;

        /// <summary>
        /// 程序启动时是否最小化到托盘
        /// </summary>
        public bool StartMinimized { get; set; } = false;
    }
}