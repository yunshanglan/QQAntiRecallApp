using System;
using System.Diagnostics;
using System.IO;

public static class Logger
{
    public enum LogLevel
    {
        DEBUG,   // 调试信息
        INFO,    // 常规信息
        WARNING, // 警告
        ERROR    // 错误
    }

    // 当前日志级别，高于此级别的不输出（可在程序入口设置）
    public static LogLevel CurrentLevel { get; set; } = LogLevel.INFO;

    // 日志写入事件，供 UI 订阅以实时显示
    public static event Action<string> OnLogWritten;

    private static readonly object _lock = new object();
    private static string _logFilePath = "app.log";

    public static void Debug(string message)
    {
        Log(LogLevel.DEBUG, message);
    }

    public static void Info(string message)
    {
        Log(LogLevel.INFO, message);
    }

    public static void Warning(string message)
    {
        Log(LogLevel.WARNING, message);
    }

    public static void Error(string message)
    {
        Log(LogLevel.ERROR, message);
    }

    public static void Error(string message, Exception ex)
    {
        Log(LogLevel.ERROR, $"{message} : {ex.Message}\n{ex.StackTrace}");
    }

    private static void Log(LogLevel level, string message)
    {
        if (level < CurrentLevel) return;

        string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";

        // 触发事件，让 UI 组件（如 LogPageControl）显示日志
        OnLogWritten?.Invoke(logEntry);
    }

    public static void SetLogFilePath(string path)
    {
        _logFilePath = path;
    }
}