using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.IsolatedStorage;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Navigation;
using Microsoft.Phone.Controls;
using Microsoft.Phone.Shell;
using MetroGopher.Models;
using MetroGopher.Resources;
using MetroGopher.Services;

namespace MetroGopher
{
    public partial class MainPage : PhoneApplicationPage
    {
        private readonly GopherClient _client = new GopherClient();
        private readonly Stack<GopherHistoryItem> _history = new Stack<GopherHistoryItem>();
        private bool _isNavigatingHistory = false;

        public MainPage()
        {
            InitializeComponent();

            // Применяем сохраненный язык к потоку при инициализации
            ApplyLanguage();

            LoadGopherPage("gopher.debene.dev", 70, "");
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            // КРИТИЧЕСКИ ВАЖНО: Обновляем ApplicationBar здесь, 
            // когда XAML полностью загружен и ApplicationBar != null
            string currentLang = AppResources.Culture != null ? AppResources.Culture.Name : "ru-RU";
            UpdateApplicationBarMenu(currentLang);
        }

        private void ApplyLanguage()
        {
            string savedLang = "ru-RU";
            if (IsolatedStorageSettings.ApplicationSettings.Contains("AppLanguage"))
            {
                savedLang = IsolatedStorageSettings.ApplicationSettings["AppLanguage"] as string;
            }

            SetAppCulture(savedLang);
        }

        private void SetAppCulture(string langCode)
        {
            var culture = new CultureInfo(langCode);

            // 1. Устанавливаем ресурсы и системную культуру потока
            AppResources.Culture = culture;
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;

            // 2. Устанавливаем язык для корневой рамки
            var rootFrame = Application.Current.RootVisual as PhoneApplicationFrame;
            if (rootFrame != null)
            {
                rootFrame.Language = XmlLanguage.GetLanguage(culture.Name);
            }

            // 3. Уведомляем привязки данных XAML через LocalizedStrings
            var localizedStrings = Application.Current.Resources["LocalizedStrings"] as LocalizedStrings;
            if (localizedStrings != null)
            {
                localizedStrings.UpdateLanguage();
            }
        }

        private void UpdateApplicationBarMenu(string currentLang)
        {
            if (ApplicationBar != null && ApplicationBar.MenuItems.Count > 0)
            {
                var menuItem = ApplicationBar.MenuItems[0] as ApplicationBarMenuItem;
                if (menuItem != null)
                {
                    // Подставляем название языка, на который переключится интерфейс
                    menuItem.Text = currentLang.StartsWith("ru") ? "English" : "Русский";
                }
            }
        }

        // Этот обработчик должен быть привязан к событию Click вашего пункта меню в XAML
        private void OnToggleLanguageClick(object sender, EventArgs e)
        {
            string currentLang = AppResources.Culture != null ? AppResources.Culture.Name : "ru-RU";
            string newLang = currentLang.StartsWith("ru") ? "en-US" : "ru-RU";

            // 1. Сохраняем новую локаль
            IsolatedStorageSettings.ApplicationSettings["AppLanguage"] = newLang;
            IsolatedStorageSettings.ApplicationSettings.Save();

            // 2. Применяем культуру
            SetAppCulture(newLang);

            // 3. Перезагружаем страницу для полного обновления XAML дерева
            NavigationService.Navigate(new Uri("/MainPage.xaml?Reset=" + Guid.NewGuid(), UriKind.Relative));
        }

        private async void LoadGopherPage(string host, int port, string selector, bool addToHistory = true)
        {
            LoadingBar.Visibility = Visibility.Visible;
            AddressBox.Text = host;

            if (addToHistory && !_isNavigatingHistory && !string.IsNullOrEmpty(host))
            {
                _history.Push(new GopherHistoryItem { Host = host, Port = port, Selector = selector });
            }

            try
            {
                string rawData = await _client.RawRequestAsync(host, port, selector);

                GopherList.ItemsSource = null;
                var items = _client.ParseMenu(rawData, host, port);
                GopherList.ItemsSource = items;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, AppResources.ErrorTitle ?? "Ошибка сети", MessageBoxButton.OK);
            }
            finally
            {
                LoadingBar.Visibility = Visibility.Collapsed;
                _isNavigatingHistory = false;
            }
        }

        private void OnGoClick(object sender, RoutedEventArgs e)
        {
            string targetHost = AddressBox.Text.Trim();
            if (!string.IsNullOrEmpty(targetHost))
            {
                LoadGopherPage(targetHost, 70, "");
            }
        }

        private async void OnItemSelected(object sender, SelectionChangedEventArgs e)
        {
            var selectedItem = GopherList.SelectedItem as GopherItem;

            if (selectedItem != null)
            {
                GopherList.SelectedItem = null;

                switch (selectedItem.ItemType)
                {
                    case GopherItemType.Directory:
                        LoadGopherPage(selectedItem.Host, selectedItem.Port, selectedItem.Selector);
                        break;

                    case GopherItemType.TextFile:
                        LoadingBar.Visibility = Visibility.Visible;
                        try
                        {
                            string textContent = await _client.RawRequestAsync(selectedItem.Host, selectedItem.Port, selectedItem.Selector);

                            TxtDocumentContent.Text = textContent;
                            MainPivot.SelectedItem = PivotDocument;
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("Не удалось загрузить файл: " + ex.Message, "Ошибка", MessageBoxButton.OK);
                        }
                        finally
                        {
                            LoadingBar.Visibility = Visibility.Collapsed;
                        }
                        break;

                    case GopherItemType.Info:
                        break;

                    case GopherItemType.Search:
                        var searchInput = new TextBox
                        {
                            Margin = new Thickness(0, 10, 0, 0),
                            InputScope = new InputScope
                            {
                                Names = { new InputScopeName { NameValue = InputScopeNameValue.Search } }
                            }
                        };

                        searchInput.Loaded += (s, ev) =>
                        {
                            searchInput.Focus();
                        };

                        var searchDialog = new CustomMessageBox
                        {
                            Caption = "ПОИСК В GOPHER",
                            Message = string.IsNullOrEmpty(selectedItem.Title) ? "Введите ваш запрос:" : selectedItem.Title,
                            Content = searchInput,
                            LeftButtonContent = "найти",
                            RightButtonContent = "отмена"
                        };

                        searchDialog.Dismissed += (s, ev) =>
                        {
                            if (ev.Result == CustomMessageBoxResult.LeftButton)
                            {
                                string query = searchInput.Text.Trim();
                                if (!string.IsNullOrEmpty(query))
                                {
                                    string searchSelector = selectedItem.Selector + "\t" + query;
                                    LoadGopherPage(selectedItem.Host, selectedItem.Port, searchSelector);
                                }
                            }
                        };

                        searchDialog.Show();
                        break;

                    default:
                        MessageBox.Show("Тип ресурса пока не поддерживается.", "Информация", MessageBoxButton.OK);
                        break;
                }
            }
        }

        protected override void OnBackKeyPress(System.ComponentModel.CancelEventArgs e)
        {
            if (MainPivot.SelectedItem == PivotDocument)
            {
                e.Cancel = true;
                MainPivot.SelectedIndex = 0;
                return;
            }

            if (_history.Count > 1)
            {
                e.Cancel = true;
                _history.Pop();

                var previousPage = _history.Peek();
                _isNavigatingHistory = true;

                LoadGopherPage(previousPage.Host, previousPage.Port, previousPage.Selector, false);
            }
            else
            {
                base.OnBackKeyPress(e);
            }
        }
    }

    public class GopherHistoryItem
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public string Selector { get; set; }
    }
}