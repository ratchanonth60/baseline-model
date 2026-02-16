using System;
using System.IO;
using BaselineMode.WPF.Core.Interfaces;

namespace BaselineMode.WPF.Infrastructure.Services
{
    public class LoggerService : ILoggerService
    {
        private readonly string _logFilePath;
        private readonly Lock _lock = new();

        public LoggerService()
        {
            string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            if (!Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }

            string fileName = $"log_{DateTime.Now:yyyyMMdd}.txt";
            _logFilePath = Path.Combine(logDir, fileName);
        }

        public void LogInfo(string message) => WriteLog("INFO", message);
        public void LogWarning(string message) => WriteLog("WARN", message);
        public void LogError(string message) => WriteLog("ERROR", message);

        public void LogException(Exception ex, string? context = null)
        {
            string message = (context != null ? $"[{context}] " : "") + ex.ToString();
            WriteLog("FATAL", message);
        }

        private void WriteLog(string level, string message)
        {
            lock (_lock)
            {
                try
                {
                    string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";
                    File.AppendAllLines(_logFilePath, [entry]);
                }
                catch
                {
                    // Fail silently to avoid crashing the app due to logging failure
                }
            }
        }
    }
}
