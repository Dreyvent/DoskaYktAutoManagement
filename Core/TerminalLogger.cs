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
        public static TerminalLogger Instance => _instance ??= new TerminalLogger();

        public event Action<string>? LogAdded;

        public List<string> History { get; } = new List<string>();

        private TerminalLogger() { }

        public void Log(string message)
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}";
            History.Add(line);
            LogAdded?.Invoke(line);
        }
    }
}
