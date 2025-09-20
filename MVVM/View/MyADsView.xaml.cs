using DoskaYkt_AutoManagement.MVVM.ViewModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace DoskaYkt_AutoManagement.MVVM.View
{
    /// <summary>
    /// Логика взаимодействия для MyADsView.xaml
    /// </summary>
    public partial class MyADsView : UserControl
    {
        public MyADsView()
        {
            InitializeComponent();
            DataContext = new MyADsViewModel();
        }
    }
}
