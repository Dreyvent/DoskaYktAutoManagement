using DoskaYkt_AutoManagement.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DoskaYkt_AutoManagement.MVVM.Model
{
    public class Ad : ObservableObject
    {
        private int _id;
        public int Id { get => _id; set => SetProperty(ref _id, value); }

        private int _accountId;
        public int AccountId { get => _accountId; set => SetProperty(ref _accountId, value); }

        private string _accountLogin = string.Empty;
        public string AccountLogin { get => _accountLogin; set => SetProperty(ref _accountLogin, value); }

        private string _title = string.Empty;
        public string Title { get => _title; set => SetProperty(ref _title, value); }

        private string _siteId = string.Empty;
        public string SiteId { get => _siteId; set => SetProperty(ref _siteId, value); }

        private bool _isAutoRaiseEnabled;
        public bool IsAutoRaiseEnabled { get => _isAutoRaiseEnabled; set => SetProperty(ref _isAutoRaiseEnabled, value); }

        private int _autoRaiseMinutes;
        public int AutoRaiseMinutes { get => _autoRaiseMinutes; set => SetProperty(ref _autoRaiseMinutes, value); }

        private DateTime? _nextUnpublishAt;
        public DateTime? NextUnpublishAt { get => _nextUnpublishAt; set => SetProperty(ref _nextUnpublishAt, value); }

        private DateTime? _nextRepublishAt;
        public DateTime? NextRepublishAt { get => _nextRepublishAt; set => SetProperty(ref _nextRepublishAt, value); }

        private int _unpublishMinutes = 10;
        public int UnpublishMinutes { get => _unpublishMinutes; set => SetProperty(ref _unpublishMinutes, value); }

        private int _republishMinutes = 20;
        public int RepublishMinutes { get => _republishMinutes; set => SetProperty(ref _republishMinutes, value); }

        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

        private bool _isPublished;
        public bool IsPublished
        {
            get => _isPublished;
            set => SetProperty(ref _isPublished, value);
        }

        private bool _isPublishedOnSite;
        public bool IsPublishedOnSite
        {
            get => _isPublishedOnSite;
            set => SetProperty(ref _isPublishedOnSite, value);
        }
    }
}
