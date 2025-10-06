using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DoskaYkt_AutoManagement.Core
{
    public class TerminalLogger
    {
        private static TerminalLogger? _instance;
        public static TerminalLogger Instance => _instance ?? (_instance = new TerminalLogger());

        public event Action<string>? LogAdded;

        public List<string> History { get; } = new List<string>();
        private readonly object _lock = new object();
        private const int MaxHistorySize = 1000;

        private TerminalLogger() { }

        public void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}";
            
            lock (_lock)
            {
                History.Add(line);
                
                // Ограничиваем размер истории для предотвращения утечек памяти
                if (History.Count > MaxHistorySize)
                {
                    History.RemoveAt(0);
                }
            }
            
            LogAdded?.Invoke(line);
            
            // Дублируем в Debug для отладки
            System.Diagnostics.Debug.WriteLine(line);
        }

        public void LogError(string message, Exception? exception = null)
        {
            var errorMessage = exception != null 
                ? $"[ERROR] {message} - {exception.Message}" 
                : $"[ERROR] {message}";
            Log(errorMessage);
        }

        public void LogWarning(string message)
        {
            Log($"[WARNING] {message}");
        }

        public void LogInfo(string message)
        {
            Log($"[INFO] {message}");
        }
    }
}
