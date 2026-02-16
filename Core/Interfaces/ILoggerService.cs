using System;

namespace BaselineMode.WPF.Core.Interfaces
{
    public interface ILoggerService
    {
        void LogInfo(string message);
        void LogWarning(string message);
        void LogError(string message);
        void LogException(Exception ex, string? context = null);
    }
}
