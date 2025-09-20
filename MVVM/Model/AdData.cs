using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

namespace DoskaYkt_AutoManagement.MVVM.Model
{
    public class AdData : INotifyPropertyChanged
    {
        private string _id;
        public string Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }
        private string _title;
        public string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(); }
        }
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }
        public int AutoRaiseMinutes { get; set; }
        public bool IsAutoRaiseEnabled { get; set; }
        public int AccountId { get; set; }
        public string AccountLogin { get; set; }
        public bool IsPublished { get; set; }
        public string SiteId { get; internal set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
