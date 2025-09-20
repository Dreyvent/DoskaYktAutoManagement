using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace DoskaYkt_AutoManagement.Core
{
    public class Account : INotifyPropertyChanged
    {
        private int _id;
        public int Id { get => _id; set { _id = value; OnPropertyChanged(); } }
        private string _login;
        public string Login { get => _login; set { _login = value; OnPropertyChanged(); } }
        private string _password;
        public string Password { get => _password; set { _password = value; OnPropertyChanged(); } }
        private bool _isCurrent;
        public bool IsCurrent { get => _isCurrent; set { _isCurrent = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class AccountManager : INotifyPropertyChanged
    {
        private static AccountManager? _instance;

        public static AccountManager Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new AccountManager();
                return _instance;
            }
        }

        public ObservableCollection<Account> Accounts { get; set; } = new ObservableCollection<Account>();

        private Account? _selectedAccount;
        public Account? SelectedAccount
        {
            get { return _selectedAccount; }
            set
            {
                _selectedAccount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedAccountDisplay));
                _ = SetCurrentAccountAsync(_selectedAccount);
            }
        }

        public string SelectedAccountDisplay => SelectedAccount?.Login ?? "Нет текущего аккаунта";

        public RelayCommand<Account> SelectAccountCommand { get; }

        private AccountManager()
        {
            Accounts = new ObservableCollection<Account>();
            SelectAccountCommand = new RelayCommand<Account>(o =>
            {
                var acc = o as Account;
                if (acc != null)
                {
                    SelectedAccount = acc;
                }
            });

            // Load accounts from DB on startup
            _ = LoadAccountsAsync();
        }

        public async Task LoadAccountsAsync()
        {
            Accounts.Clear();
            var accounts = await DatabaseHelper.GetAccountsAsync();
            foreach (var (id, login, password, isCurrent) in accounts)
            {
                var acc = new Account { Id = id, Login = login, Password = password, IsCurrent = isCurrent };
                Accounts.Add(acc);
                if (isCurrent)
                    _selectedAccount = acc;
            }
            OnPropertyChanged(nameof(SelectedAccount));
            OnPropertyChanged(nameof(SelectedAccountDisplay));
        }

        public async Task AddAccountAsync(string login, string password)
        {
            bool isFirst = Accounts.Count == 0;
            int id = await DatabaseHelper.AddAccountAsync(login, password, isFirst);
            var acc = new Account { Id = id, Login = login, Password = password, IsCurrent = isFirst };
            Accounts.Add(acc);
            if (isFirst)
            {
                SelectedAccount = acc;
            }

            Core.TerminalLogger.Instance.Log($"[Account] Добавлен аккаунт Login='{login}'");
        }

        public async Task DeleteAccountAsync(Account acc)
        {
            if (acc == null) return;
            await DatabaseHelper.DeleteAccountAsync(acc.Id);
            Accounts.Remove(acc);

            Core.TerminalLogger.Instance.Log($"[Account] Удалён аккаунт Login='{acc.Login}'");

            if (acc.IsCurrent && Accounts.Count > 0)
            {
                SelectedAccount = Accounts.First();
            }
            else if (Accounts.Count == 0)
            {
                SelectedAccount = null;
            }
        }

        public async Task UpdateAccountAsync(Account acc)
        {
            if (acc == null) return;
            await DatabaseHelper.UpdateAccountAsync(acc.Id, acc.Login, acc.Password);

            Core.TerminalLogger.Instance.Log($"[Account] Обновлён аккаунт Login='{acc.Login}'");
        }

        public async Task SetCurrentAccountAsync(Account acc)
        {
            if (acc == null) return;
            await DatabaseHelper.SetCurrentAccountAsync(acc.Id);
            foreach (var a in Accounts)
                a.IsCurrent = (a == acc);

            Core.TerminalLogger.Instance.Log($"[Account] Текущий аккаунт установлен на Login='{acc.Login}'");
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
