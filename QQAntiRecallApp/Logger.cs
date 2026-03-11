using System;

namespace QQAntiRecallApp
{
    public static class Logger
    {
        private static readonly object _lock = new object();

        public enum LogLevel
        {
            Info,
            Warning,
            Error
        }

        public static event Action<string> OnLogWritten;

        public static void Info(string message) => Write(LogLevel.Info, message);
        public static void Warning(string message) => Write(LogLevel.Warning, message);
        public static void Error(string message) => Write(LogLevel.Error, message);
        public static void Error(string message, Exception ex) => Write(LogLevel.Error, $"{message}\n{ex}");

        private static void Write(LogLevel level, string message)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string logEntry = $"[{timestamp}] [{level}] {message}";
            // 仅触发事件，不再输出到控制台
            OnLogWritten?.Invoke(logEntry);
        }
    }
}