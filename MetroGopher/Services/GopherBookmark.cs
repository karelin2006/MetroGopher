using System.ComponentModel;
using System.Windows;

namespace MetroGopher.Services
{
    public class GopherBookmark : INotifyPropertyChanged
    {
        private string _title;
        private string _host;
        private int _port = 70;
        private string _selector;
        private GopherItemType _itemType;
        private bool _isAccordionExpanded;

        public string Title
        {
            get { return _title; }
            set { if (_title != value) { _title = value; OnPropertyChanged("Title"); } }
        }

        public string Host
        {
            get { return _host; }
            set { if (_host != value) { _host = value; OnPropertyChanged("Host"); } }
        }

        public int Port
        {
            get { return _port; }
            set { if (_port != value) { _port = value; OnPropertyChanged("Port"); } }
        }

        public string Selector
        {
            get { return _selector; }
            set { if (_selector != value) { _selector = value; OnPropertyChanged("Selector"); } }
        }

        public GopherItemType ItemType
        {
            get { return _itemType; }
            set { if (_itemType != value) { _itemType = value; OnPropertyChanged("ItemType"); } }
        }

        public bool IsAccordionExpanded
        {
            get { return _isAccordionExpanded; }
            set
            {
                if (_isAccordionExpanded != value)
                {
                    _isAccordionExpanded = value;
                    OnPropertyChanged("IsAccordionExpanded");
                    OnPropertyChanged("AccordionVisibility");
                }
            }
        }

        public Visibility AccordionVisibility
        {
            get { return _isAccordionExpanded ? Visibility.Visible : Visibility.Collapsed; }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}