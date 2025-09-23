using System;

namespace DoskaYkt_AutoManagement.Core
{
    public static class SiteAutomationProvider
    {
        private static ISiteAutomation _current;

        public static ISiteAutomation Current
        {
            get
            {
                if (_current != null) return _current;
                _current = Resolve();
                return _current;
            }
        }

        public static void Reset() => _current = null;

        private static ISiteAutomation Resolve()
        {
            return DoskaYktService.Instance;
        }
    }
}


