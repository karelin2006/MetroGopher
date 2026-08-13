using System.ComponentModel;
using MetroGopher.Resources;

namespace MetroGopher
{
    /// <summary>
    /// Provides access to string resources with dynamic UI update support.
    /// </summary>
    public class LocalizedStrings : INotifyPropertyChanged
    {
        private static AppResources _localizedResources = new AppResources();

        public AppResources LocalizedResources
        {
            get { return _localizedResources; }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Вызывайте этот метод после смены AppResources.Culture,
        /// чтобы уведомить XAML-привязки о необходимости обновить тексты.
        /// </summary>
        public void UpdateLanguage()
        {
            var handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs("LocalizedResources"));
            }
        }
    }
}