using DoskaYkt_AutoManagement.MVVM.ViewModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace DoskaYkt_AutoManagement
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            // Устанавливаем DataContext на MainViewModel
            DataContext = new MainViewModel();

            // Подписываемся на события логгера и пересылаем в текстбокс
            Core.TerminalLogger.Instance.LogAdded += AppendLog;
        }

        private void Log(string message)
        {
            TerminalLogs.AppendText($"{DateTime.Now:HH:mm:ss} - {message}\n");
            TerminalLogs.ScrollToEnd();
        }

        public void AppendLog(string message)
        {
            if (Dispatcher.CheckAccess())
            {
                TerminalLogs.AppendText(message + "\n");
                TerminalLogs.ScrollToEnd();
            }
            else
            {
                Dispatcher.Invoke(() => AppendLog(message));
            }
        }

        private void Border_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void ExitBtn_Click(object sender, RoutedEventArgs e)
        {
            // Реальный выход из программы
            Application.Current.Shutdown();
        }

        private void MinimizeBtn_Click(object sender, RoutedEventArgs e)
        {
            // Просто минимизируем — дальше перехватит App.StateChanged
            WindowState = WindowState.Minimized;
        }
    }
}