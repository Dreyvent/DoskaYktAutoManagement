using DoskaYkt_AutoManagement.Core;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace DoskaYkt_AutoManagement.MVVM.ViewModel
{
    public class MyAccountsViewModel : ObservableObject
    {
        // Используем Account из Core (AccountManager.Instance.Accounts)
        public ObservableCollection<Account> Accounts => AccountManager.Instance.Accounts;

        private Account? _editingAccount;
        public Account? EditingAccount
        {
            get => _editingAccount;
            set => SetProperty(ref _editingAccount, value);
        }

        private string _login = string.Empty;
        public string Login
        {
            get => _login;
            set => SetProperty(ref _login, value);
        }

        private string _password = string.Empty;
        public string Password
        {
            get => _password;
            set => SetProperty(ref _password, value);
        }

        private bool _showPassword;
        public bool ShowPassword
        {
            get => _showPassword;
            set => SetProperty(ref _showPassword, value);
        }

        public RelayCommand<object> SaveAccountCommand { get; }
        public RelayCommand<object> ToggleShowPasswordCommand { get; }
        public RelayCommand<Account> DeleteAccountCommand { get; }
        public RelayCommand<Account> SelectAccountCommand { get; }
        public RelayCommand<Account> EditAccountCommand { get; }

        public MyAccountsViewModel()
        {
            // async void - нормально для команд, которые вызывают асинхронную логику
            SaveAccountCommand = new RelayCommand<object>(async _ =>
            {
                if (!string.IsNullOrWhiteSpace(Login) && !string.IsNullOrWhiteSpace(Password))
                {
                    if (EditingAccount == null)
                    {
                        // добавление
                        await AccountManager.Instance.AddAccountAsync(Login, Password);
                        Core.TerminalLogger.Instance.Log($"[Account] Добавлен новый аккаунт Login='{Login}'");
                    }
                    else
                    {
                        // редактирование
                        EditingAccount.Login = Login;
                        EditingAccount.Password = Password;
                        await AccountManager.Instance.UpdateAccountAsync(EditingAccount);

                        Core.TerminalLogger.Instance.Log($"[Account] Изменён аккаунт Id={EditingAccount.Id}");

                        EditingAccount = null; // сброс режима редактирования
                    }

                    Login = string.Empty;
                    Password = string.Empty;
                }
            });

            ToggleShowPasswordCommand = new RelayCommand<object>(_ =>
            {
                ShowPassword = !ShowPassword;
            });

            DeleteAccountCommand = new RelayCommand<Account>(async acc =>
            {
                if (acc != null)
                {
                    await AccountManager.Instance.DeleteAccountAsync(acc);
                }
            });

            SelectAccountCommand = new RelayCommand<Account>(acc =>
            {
                if (acc != null)
                    AccountManager.Instance.SelectedAccount = acc;
            });

            EditAccountCommand = new RelayCommand<Account>(acc =>
            {
                if (acc != null)
                {
                    EditingAccount = acc;
                    Login = acc.Login;
                    Password = acc.Password;
                }
            });
        }

        public class EditModeToContentConverter : IMultiValueConverter
        {
            public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                return values[0] == null ? "Добавить аккаунт" : "Сохранить изменения";
            }

            public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
            {
                throw new NotImplementedException();
            }
        }

    }
}
