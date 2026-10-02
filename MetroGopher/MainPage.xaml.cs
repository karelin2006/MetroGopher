using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.IsolatedStorage;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Phone.Controls;
using Microsoft.Phone.Tasks;
using MetroGopher.Services;

namespace MetroGopher
{
    public partial class MainPage : PhoneApplicationPage
    {
        private readonly GopherClient _client = new GopherClient();

        // Временная история ТОЛЬКО для аппаратной кнопки "Назад"
        private readonly Stack<GopherHistoryItem> _historyStack = new Stack<GopherHistoryItem>();
        private bool _isNavigatingHistory = false;

        private string _currentHost = "gopher.debene.dev";
        private int _currentPort = 70;
        private string _currentSelector = "";

        private GopherItem _currentDocument = null;

        private readonly System.Windows.Threading.DispatcherTimer _autoRefreshTimer = new System.Windows.Threading.DispatcherTimer();

        private readonly string[] _knownGopherServers =
        {
            "gopher.debene.dev", "gopher.floodgap.com", "sdf.org", "gopher.viste.fr",
            "bitreich.org", "quux.org", "gopher.black",
            "gopher.navigo.com", "1436.ninja", "gopher.club",
            "gopher.ddis.ch", "gopher.fman.com", "hngopher.com",
            "gopher.linkerror.com", "magical.city", "gopher.tildeverse.org",
            "gopher.icu", "gopher.top", "gopher.me",
            "phreaknet.org", "gopher.somnolescent.net", "gopher.stgraber.org",
            "gopher.osmz.ru", "gopher.space"
        };

        private ObservableCollection<GopherBookmark> _bookmarks = new ObservableCollection<GopherBookmark>();
        public ObservableCollection<GopherBookmark> Bookmarks
        {
            get { return _bookmarks; }
            set { _bookmarks = value; }
        }

        private ObservableCollection<GopherHistoryItem> _historyList = new ObservableCollection<GopherHistoryItem>();
        public ObservableCollection<GopherHistoryItem> History
        {
            get { return _historyList; }
            set { _historyList = value; }
        }

        private ObservableCollection<GopherItem> _gopherItems = new ObservableCollection<GopherItem>();

        private const string BookmarksKey = "SavedBookmarks";
        private const string HistoryKey = "SavedHistory";

        public MainPage()
        {
            InitializeComponent();
            LoadData();

            _autoRefreshTimer.Interval = TimeSpan.FromSeconds(5);
            _autoRefreshTimer.Tick += (s, ev) =>
            {
                SilentRefreshDocument();
            };

            UpdateAddressSuggestions();

            GopherList.ItemsSource = _gopherItems;
            LoadGopherPage(_currentHost, _currentPort, _currentSelector);
        }

        #region Отображение данных

        private void DisplayLongText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                DocumentListBox.ItemsSource = null;
                return;
            }

            var rawLines = text.Split(new[] { '\n' }, StringSplitOptions.None);

            // Умный анализатор: определяем, является ли документ ASCII-картой/схемой
            bool isAsciiMap = DetectIfAsciiMap(rawLines);

            var items = new List<FormattedTextLine>();
            foreach (var line in rawLines)
            {
                items.Add(new FormattedTextLine
                {
                    Text = line,
                    IsAsciiArt = isAsciiMap
                });
            }

            DocumentListBox.ItemsSource = items;
            MainPivot.SelectedItem = PivotDocument;
        }

        private bool DetectIfAsciiMap(string[] lines)
        {
            int asciiIndicators = 0;
            int sampleCount = Math.Min(lines.Length, 60);

            for (int i = 0; i < sampleCount; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Характерные символы путей, стрелок, станций и ASCII-графики
                int mapChars = line.Count(c => c == '=' || c == '|' || c == '+' || c == '-' || c == '/' || c == '\\' || c == '[' || c == ']' || c == '<' || c == '>');

                if (mapChars >= 6 || line.Contains("===") || line.Contains("---") || line.Contains("..."))
                {
                    asciiIndicators++;
                }
            }

            return asciiIndicators > (sampleCount * 0.15);
        }

        #endregion

        #region Работа с хранилищем (Закладки и История)

        private void LoadData()
        {
            var settings = IsolatedStorageSettings.ApplicationSettings;

            if (settings.Contains(BookmarksKey))
            {
                var savedBookmarks = settings[BookmarksKey] as List<string>;
                if (savedBookmarks != null)
                {
                    Bookmarks.Clear();
                    foreach (var item in savedBookmarks)
                    {
                        var parts = item.Split('|');
                        if (parts.Length >= 5)
                        {
                            int port = 70;
                            int.TryParse(parts[2], out port);

                            GopherItemType itemType = GopherItemType.Unknown;
                            try { itemType = (GopherItemType)Enum.Parse(typeof(GopherItemType), parts[4]); }
                            catch { }

                            var bm = new GopherBookmark
                            {
                                Title = parts[0],
                                Host = parts[1],
                                Port = port,
                                Selector = parts[3],
                                ItemType = itemType
                            };
                            Bookmarks.Add(bm);
                        }
                    }
                }
            }

            if (settings.Contains(HistoryKey))
            {
                var savedHistory = settings[HistoryKey] as List<string>;
                if (savedHistory != null)
                {
                    History.Clear();
                    foreach (var item in savedHistory)
                    {
                        var parts = item.Split('|');
                        if (parts.Length >= 3)
                        {
                            int port = 70;
                            int.TryParse(parts[1], out port);

                            GopherItemType itemType = GopherItemType.Directory;
                            if (parts.Length >= 4)
                            {
                                try { itemType = (GopherItemType)Enum.Parse(typeof(GopherItemType), parts[3]); }
                                catch { }
                            }

                            History.Add(new GopherHistoryItem { Host = parts[0], Port = port, Selector = parts[2], ItemType = itemType });
                        }
                    }
                }
            }

            BookmarksList.ItemsSource = Bookmarks;
            HistoryList.ItemsSource = History;
        }

        private void SaveData()
        {
            var settings = IsolatedStorageSettings.ApplicationSettings;

            settings[BookmarksKey] = new List<string>(
                Bookmarks.Select(b => b.Title + "|" + b.Host + "|" + b.Port + "|" + b.Selector + "|" + b.ItemType));

            settings[HistoryKey] = new List<string>(
                History.Select(h => h.Host + "|" + h.Port + "|" + h.Selector + "|" + h.ItemType));

            settings.Save();
        }

        private void SaveToPermanentHistory(string host, int port, string selector, GopherItemType type)
        {
            if (string.IsNullOrEmpty(host) || _isNavigatingHistory) return;

            var existing = History.FirstOrDefault(h => h.Host == host && h.Port == port && h.Selector == selector);
            if (existing != null)
            {
                History.Remove(existing);
            }

            History.Insert(0, new GopherHistoryItem { Host = host, Port = port, Selector = selector, ItemType = type });

            if (History.Count > 100)
            {
                History.RemoveAt(History.Count - 1);
            }

            SaveData();
            UpdateAddressSuggestions();
        }

        #endregion

        #region Адресная строка и навигация

        private void OnGoClick(object sender, RoutedEventArgs e)
        {
            string input = AddressBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(input)) return;

            if (!input.Contains(".") && !input.Contains(":") && !input.Contains("/"))
            {
                string searchHost = "gopher.floodgap.com";
                int searchPort = 70;
                string searchSelector = "/v2/vs\t" + input;
                LoadGopherPage(searchHost, searchPort, searchSelector);
                return;
            }

            string host;
            int port;
            string selector;
            ParseAddress(input, out host, out port, out selector);
            LoadGopherPage(host, port, selector);
        }

        private void ParseAddress(string input, out string host, out int port, out string selector)
        {
            host = _currentHost;
            port = _currentPort;
            selector = "";

            if (string.IsNullOrWhiteSpace(input))
                return;

            string s = input.Trim();

            if (s.StartsWith("gopher://", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(9);

            int slash = s.IndexOf('/');
            if (slash >= 0)
            {
                selector = s.Substring(slash);
                s = s.Substring(0, slash);
            }

            int colon = s.LastIndexOf(':');
            if (colon > 0 && colon < s.Length - 1)
            {
                host = s.Substring(0, colon);
                int.TryParse(s.Substring(colon + 1), out port);
                if (port <= 0) port = 70;
            }
            else if (!string.IsNullOrEmpty(s))
            {
                host = s;
            }
        }

        private async void LoadGopherPage(string host, int port, string selector, bool addToHistory = true, bool forceRefresh = false)
        {
            _currentHost = host;
            _currentPort = port;
            _currentSelector = selector;
            _currentDocument = null;

            LoadingBar.Visibility = Visibility.Visible;
            _gopherItems.Clear();

            string displaySelector = selector;
            if (displaySelector.Contains("\t"))
            {
                displaySelector = displaySelector.Split('\t')[0];
            }

            if (string.IsNullOrEmpty(displaySelector))
                AddressBox.Text = port == 70 ? host : host + ":" + port;
            else
                AddressBox.Text = port == 70 ? host + displaySelector : host + ":" + port + displaySelector;

            if (addToHistory && !_isNavigatingHistory && !string.IsNullOrEmpty(host))
            {
                _historyStack.Push(new GopherHistoryItem { Host = host, Port = port, Selector = selector, ItemType = GopherItemType.Directory });
                SaveToPermanentHistory(host, port, selector, GopherItemType.Directory);
            }

            try
            {
                string rawData = await _client.FetchTextAsync(host, port, selector);
                var items = _client.ParseMenu(rawData, host, port);

                foreach (var item in items)
                    _gopherItems.Add(item);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Network Error", MessageBoxButton.OK);
            }
            finally
            {
                LoadingBar.Visibility = Visibility.Collapsed;
                _isNavigatingHistory = false;
            }
        }

        #endregion

        #region Диспетчеризация форматов элементов Gopher

        private async void OnItemSelected(object sender, SelectionChangedEventArgs e)
        {
            var selectedItem = GopherList.SelectedItem as GopherItem;
            if (selectedItem == null) return;

            GopherList.SelectedItem = null;
            await RouteGopherItemAsync(selectedItem);
        }

        private async Task RouteGopherItemAsync(GopherItem item)
        {
            if (item == null || !item.IsClickable)
                return;

            switch (item.ItemType)
            {
                case GopherItemType.Directory:
                    LoadGopherPage(item.Host, item.Port, item.Selector);
                    break;

                case GopherItemType.TextFile:
                    await OpenTextFileAsync(item);
                    break;

                case GopherItemType.Search:
                    ShowSearchDialog(item);
                    break;

                case GopherItemType.Image:
                    await OpenImageAsync(item);
                    break;

                case GopherItemType.Audio:
                    await PlayAudioAsync(item);
                    break;

                case GopherItemType.HtmlLink:
                    OpenWebLink(item);
                    break;

                case GopherItemType.Telnet:
                case GopherItemType.Tn3270:
                    OpenTelnet(item);
                    break;

                case GopherItemType.CSOPhone:
                    ShowCsoDialog(item);
                    break;

                case GopherItemType.Error:
                    MessageBox.Show(item.Title ?? "Server reported error", "Gopher Error (Type 3)", MessageBoxButton.OK);
                    break;

                case GopherItemType.Binary:
                case GopherItemType.DosBinary:
                case GopherItemType.BinHex:
                case GopherItemType.Uuencoded:
                case GopherItemType.Video:
                case GopherItemType.Document:
                    await DownloadAndSaveFileAsync(item);
                    break;

                case GopherItemType.Info:
                    break;

                case GopherItemType.Unknown:
                default:
                    try
                    {
                        await OpenTextFileAsync(item);
                    }
                    catch
                    {
                        var result = MessageBox.Show(
                            "Неизвестный тип ресурса.\nСкачать как двоичный файл?",
                            item.Title ?? "File",
                            MessageBoxButton.OKCancel);

                        if (result == MessageBoxResult.OK)
                            await DownloadAndSaveFileAsync(item);
                    }
                    break;
            }
        }

        #endregion

        #region Обработчики специализированных форматов

        private async Task OpenTextFileAsync(GopherItem item)
        {
            LoadingBar.Visibility = Visibility.Visible;
            DocumentListBox.ItemsSource = new List<FormattedTextLine> { new FormattedTextLine { Text = "Loading...", IsAsciiArt = false } };

            SaveToPermanentHistory(item.Host, item.Port, item.Selector, item.ItemType);

            try
            {
                string raw = await _client.FetchTextAsync(item.Host, item.Port, item.Selector);
                string clean = _client.CleanTextContent(raw);

                if (IsProbablyBinary(clean))
                {
                    var res = MessageBox.Show(
                        "Файл содержит нетекстовые данные.\nСкачать как бинарный файл?",
                        item.Title ?? "File",
                        MessageBoxButton.OKCancel);

                    if (res == MessageBoxResult.OK)
                        await DownloadAndSaveFileAsync(item);

                    DocumentListBox.ItemsSource = null;
                    return;
                }

                DisplayLongText(clean);
                _currentDocument = item;
            }
            catch (Exception ex)
            {
                DocumentListBox.ItemsSource = new List<FormattedTextLine> { new FormattedTextLine { Text = "Ошибка загрузки.", IsAsciiArt = false } };
                MessageBox.Show("Не удалось открыть файл:\n" + ex.Message, "Error", MessageBoxButton.OK);
            }
            finally
            {
                LoadingBar.Visibility = Visibility.Collapsed;
            }
        }

        private async Task OpenImageAsync(GopherItem item)
        {
            LoadingBar.Visibility = Visibility.Visible;
            SaveToPermanentHistory(item.Host, item.Port, item.Selector, item.ItemType);

            try
            {
                byte[] imgBytes = await _client.RawRequestBytesAsync(item.Host, item.Port, item.Selector);
                if (imgBytes == null || imgBytes.Length == 0)
                {
                    MessageBox.Show("Сервер вернул пустое изображение.", "Image Viewer", MessageBoxButton.OK);
                    return;
                }

                var bitmap = new BitmapImage();
                using (var ms = new MemoryStream(imgBytes))
                {
                    bitmap.SetSource(ms);
                }

                var imageControl = new Image
                {
                    Source = bitmap,
                    Stretch = System.Windows.Media.Stretch.Uniform,
                    MaxHeight = 600
                };

                var scroll = new ScrollViewer
                {
                    Content = imageControl,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                };

                var imageDialog = new CustomMessageBox
                {
                    Caption = item.Title ?? "IMAGE VIEWER",
                    Message = string.Format("{0}x{1} ({2})", bitmap.PixelWidth, bitmap.PixelHeight, FormatSize(imgBytes.Length)),
                    Content = scroll,
                    LeftButtonContent = "save",
                    RightButtonContent = "close"
                };

                imageDialog.Dismissed += async (s, ev) =>
                {
                    if (ev.Result == CustomMessageBoxResult.LeftButton)
                    {
                        await DownloadAndSaveFileAsync(item);
                    }
                };

                imageDialog.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось загрузить картинку:\n" + ex.Message, "Image Error", MessageBoxButton.OK);
            }
            finally
            {
                LoadingBar.Visibility = Visibility.Collapsed;
            }
        }

        private async Task PlayAudioAsync(GopherItem item)
        {
            LoadingBar.Visibility = Visibility.Visible;
            try
            {
                byte[] audioBytes = await _client.RawRequestBytesAsync(item.Host, item.Port, item.Selector);
                if (audioBytes == null || audioBytes.Length == 0)
                {
                    MessageBox.Show("Файл пуст или недоступен.", "Audio Error", MessageBoxButton.OK);
                    return;
                }

                string tempFileName = "temp_audio.mp3";
                using (var store = IsolatedStorageFile.GetUserStoreForApplication())
                {
                    if (store.FileExists(tempFileName))
                        store.DeleteFile(tempFileName);

                    using (var stream = store.CreateFile(tempFileName))
                    {
                        stream.Write(audioBytes, 0, audioBytes.Length);
                    }

                    using (var readStream = store.OpenFile(tempFileName, FileMode.Open, FileAccess.Read))
                    {
                        AudioPlayer.SetSource(readStream);
                        AudioPlayer.Play();
                    }
                }

                var res = MessageBox.Show(
                    string.Format("Играет: {0}\nРазмер: {1}\n\nОстановить воспроизведение или сохранить?",
                        item.Title ?? "Audio", FormatSize(audioBytes.Length)),
                    "Gopher Music Player",
                    MessageBoxButton.OKCancel);

                if (res == MessageBoxResult.Cancel)
                {
                    AudioPlayer.Stop();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка воспроизведения:\n" + ex.Message, "Player Error", MessageBoxButton.OK);
            }
            finally
            {
                LoadingBar.Visibility = Visibility.Collapsed;
            }
        }

        private void OpenWebLink(GopherItem item)
        {
            string url = item.Selector;
            if (string.IsNullOrWhiteSpace(url)) return;

            if (url.StartsWith("URL:", StringComparison.OrdinalIgnoreCase))
                url = url.Substring(4);

            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "http://" + url;
            }

            try
            {
                var webBrowserTask = new WebBrowserTask { Uri = new Uri(url, UriKind.Absolute) };
                webBrowserTask.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Неверная ссылка:\n" + ex.Message, "Browser Error", MessageBoxButton.OK);
            }
        }

        private async void OpenTelnet(GopherItem item)
        {
            string telnetUri = string.Format("telnet://{0}:{1}", item.Host, item.Port);
            try
            {
                await Windows.System.Launcher.LaunchUriAsync(new Uri(telnetUri));
            }
            catch
            {
                MessageBox.Show(
                    string.Format("Сессия Telnet:\nХост: {0}\nПорт: {1}\nЛогин/селектор: {2}\n\nДля подключения воспользуйтесь внешним клиентом Telnet.", item.Host, item.Port, item.Selector),
                    "Telnet Connection",
                    MessageBoxButton.OK);
            }
        }

        private void ShowCsoDialog(GopherItem item)
        {
            var searchInput = new TextBox
            {
                Margin = new Thickness(0, 10, 0, 0),
                InputScope = new InputScope
                {
                    Names = { new InputScopeName { NameValue = InputScopeNameValue.Search } }
                }
            };
            searchInput.Loaded += (s, ev) => searchInput.Focus();

            var dialog = new CustomMessageBox
            {
                Caption = "CSO PHONEBOOK (RFC 1436)",
                Message = string.Format("Поиск абонента на {0}:", item.Host),
                Content = searchInput,
                LeftButtonContent = "search",
                RightButtonContent = "cancel"
            };

            dialog.Dismissed += async (s, ev) =>
            {
                if (ev.Result == CustomMessageBoxResult.LeftButton)
                {
                    string query = searchInput.Text.Trim();
                    if (!string.IsNullOrEmpty(query))
                    {
                        await QueryCsoServerAsync(item.Host, item.Port, query);
                    }
                }
            };
            dialog.Show();
        }

        private async Task QueryCsoServerAsync(string host, int port, string query)
        {
            LoadingBar.Visibility = Visibility.Visible;
            try
            {
                string csoQuery = "query " + query;
                string result = await _client.FetchTextAsync(host, port, csoQuery);
                DisplayLongText(result);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка CSO сервера:\n" + ex.Message, "CSO Error", MessageBoxButton.OK);
            }
            finally
            {
                LoadingBar.Visibility = Visibility.Collapsed;
            }
        }

        private void ShowSearchDialog(GopherItem selectedItem)
        {
            var searchInput = new TextBox
            {
                Margin = new Thickness(0, 10, 0, 0),
                InputScope = new InputScope
                {
                    Names = { new InputScopeName { NameValue = InputScopeNameValue.Search } }
                }
            };
            searchInput.Loaded += (s, ev) => searchInput.Focus();

            var searchDialog = new CustomMessageBox
            {
                Caption = "GOPHER SEARCH (TYPE 7)",
                Message = string.IsNullOrEmpty(selectedItem.Title) ? "Введите поисковый запрос:" : selectedItem.Title,
                Content = searchInput,
                LeftButtonContent = "search",
                RightButtonContent = "cancel"
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
        }

        #endregion

        #region Загрузка бинарных файлов и сохранение

        private async Task DownloadAndSaveFileAsync(GopherItem item)
        {
            LoadingBar.Visibility = Visibility.Visible;
            SaveToPermanentHistory(item.Host, item.Port, item.Selector, item.ItemType);

            try
            {
                byte[] data = await _client.RawRequestBytesAsync(item.Host, item.Port, item.Selector);
                string fileName = MakeSafeFileName(item.Title, item.ItemType);

                using (var store = IsolatedStorageFile.GetUserStoreForApplication())
                {
                    if (!store.DirectoryExists("Downloads"))
                        store.CreateDirectory("Downloads");

                    string fullPath = "Downloads\\" + fileName;
                    int counter = 1;
                    string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                    string ext = Path.GetExtension(fileName);

                    while (store.FileExists(fullPath))
                    {
                        fullPath = string.Format("Downloads\\{0}_{1}{2}", nameWithoutExt, counter, ext);
                        counter++;
                    }

                    using (var stream = store.CreateFile(fullPath))
                    {
                        stream.Write(data, 0, data.Length);
                    }
                }

                MessageBox.Show(
                    string.Format("Файл сохранен в Downloads:\n{0}\n\nРазмер: {1}", fileName, FormatSize(data.Length)),
                    "Загрузка завершена",
                    MessageBoxButton.OK);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка скачивания:\n" + ex.Message, "Error", MessageBoxButton.OK);
            }
            finally
            {
                LoadingBar.Visibility = Visibility.Collapsed;
            }
        }

        private string MakeSafeFileName(string title, GopherItemType type)
        {
            if (string.IsNullOrWhiteSpace(title))
                title = "file";

            foreach (char c in Path.GetInvalidFileNameChars())
                title = title.Replace(c, '_');

            title = title.Trim();
            if (title.Length > 60) title = title.Substring(0, 60);

            string ext = Path.GetExtension(title);
            if (string.IsNullOrEmpty(ext))
            {
                switch (type)
                {
                    case GopherItemType.Image: ext = ".jpg"; break;
                    case GopherItemType.Audio: ext = ".mp3"; break;
                    case GopherItemType.Video: ext = ".mp4"; break;
                    case GopherItemType.Binary:
                    case GopherItemType.DosBinary: ext = ".bin"; break;
                    case GopherItemType.BinHex: ext = ".hqx"; break;
                    case GopherItemType.Uuencoded: ext = ".uue"; break;
                    case GopherItemType.Document: ext = ".pdf"; break;
                    default: ext = ".dat"; break;
                }
                title += ext;
            }

            return title;
        }

        private string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0") + " KB";
            return (bytes / (1024.0 * 1024.0)).ToString("0.00") + " MB";
        }

        private bool IsProbablyBinary(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            int bad = 0;
            int checkLen = Math.Min(text.Length, 2048);
            for (int i = 0; i < checkLen; i++)
            {
                char c = text[i];
                if (c == 0 || (c < 32 && c != '\n' && c != '\r' && c != '\t'))
                    bad++;
            }
            return bad > checkLen / 20;
        }

        #endregion

        #region Управление Pivot, История, Закладки

        protected override void OnBackKeyPress(System.ComponentModel.CancelEventArgs e)
        {
            if (MainPivot.SelectedItem == PivotDocument)
            {
                e.Cancel = true;
                MainPivot.SelectedIndex = 0;
                _currentDocument = null;
                _autoRefreshTimer.Stop();
                return;
            }

            if (_historyStack.Count > 1)
            {
                e.Cancel = true;
                _historyStack.Pop();
                var previousPage = _historyStack.Peek();
                _isNavigatingHistory = true;
                LoadGopherPage(previousPage.Host, previousPage.Port, previousPage.Selector, false);
            }
            else
            {
                base.OnBackKeyPress(e);
            }
        }

        private void UpdateAddressSuggestions()
        {
            var suggestions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var server in _knownGopherServers)
            {
                suggestions.Add(server);
            }

            if (History != null)
            {
                foreach (var item in History)
                {
                    if (item != null && !string.IsNullOrWhiteSpace(item.Host))
                    {
                        string fullAddress = item.Port == 70
                            ? item.Host + item.Selector
                            : item.Host + ":" + item.Port + item.Selector;

                        suggestions.Add(fullAddress);
                    }
                }
            }

            if (_bookmarks != null)
            {
                foreach (var bookmark in _bookmarks)
                {
                    if (bookmark != null && !string.IsNullOrWhiteSpace(bookmark.Host))
                    {
                        string fullAddress = bookmark.Port == 70
                            ? bookmark.Host + bookmark.Selector
                            : bookmark.Host + ":" + bookmark.Port + bookmark.Selector;

                        suggestions.Add(fullAddress);

                        if (!string.IsNullOrWhiteSpace(bookmark.Title))
                        {
                            suggestions.Add(bookmark.Title);
                        }
                    }
                }
            }

            AddressBox.ItemsSource = suggestions.OrderBy(s => s).ToList();
        }

        private async void RefreshCurrentDocument()
        {
            if (_currentDocument == null) return;

            LoadingBar.Visibility = Visibility.Visible;
            DocumentListBox.ItemsSource = new List<FormattedTextLine> { new FormattedTextLine { Text = "Loading...", IsAsciiArt = false } };

            try
            {
                string raw = await _client.FetchTextAsync(_currentDocument.Host, _currentDocument.Port, _currentDocument.Selector);
                string clean = _client.CleanTextContent(raw);

                if (IsProbablyBinary(clean))
                {
                    var res = MessageBox.Show(
                        "Файл не является текстовым.\nСкачать как бинарный?",
                        _currentDocument.Title ?? "File",
                        MessageBoxButton.OKCancel);

                    if (res == MessageBoxResult.OK)
                        await DownloadAndSaveFileAsync(_currentDocument);

                    DocumentListBox.ItemsSource = null;
                    return;
                }

                DisplayLongText(clean);
            }
            catch (Exception ex)
            {
                DocumentListBox.ItemsSource = new List<FormattedTextLine> { new FormattedTextLine { Text = "Load failed.", IsAsciiArt = false } };
                MessageBox.Show("Не удалось обновить документ:\n" + ex.Message, "Error", MessageBoxButton.OK);
            }
            finally
            {
                LoadingBar.Visibility = Visibility.Collapsed;
            }
        }

        private async void SilentRefreshDocument()
        {
            if (_currentDocument == null || MainPivot.SelectedItem != PivotDocument) return;

            try
            {
                string raw = await _client.FetchTextAsync(_currentDocument.Host, _currentDocument.Port, _currentDocument.Selector);
                string clean = _client.CleanTextContent(raw);

                if (!IsProbablyBinary(clean))
                {
                    DisplayLongText(clean);
                }
            }
            catch
            {
                // Подавляем фоновые ошибки
            }
        }

        private void OnBookmarkClick(object sender, EventArgs e)
        {
            GopherBookmark bm;

            if (_currentDocument != null && MainPivot.SelectedItem == PivotDocument)
            {
                bm = new GopherBookmark
                {
                    Title = _currentDocument.Title ?? "Document",
                    Host = _currentDocument.Host,
                    Port = _currentDocument.Port,
                    Selector = _currentDocument.Selector,
                    ItemType = _currentDocument.ItemType
                };
            }
            else
            {
                string url = AddressBox.Text;
                bm = new GopherBookmark
                {
                    Title = string.IsNullOrWhiteSpace(url) ? "Page" : url,
                    Host = _currentHost,
                    Port = _currentPort,
                    Selector = _currentSelector,
                    ItemType = GopherItemType.Directory
                };
            }

            Bookmarks.Add(bm);
            SaveData();
            UpdateAddressSuggestions();

            MessageBox.Show("Страница сохранена в закладки!", "Закладки", MessageBoxButton.OK);
        }

        private void OnPivotSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MainPivot.SelectedIndex == 1)
            {
                LoadBookmarks();
                _autoRefreshTimer.Stop();
            }
            else if (MainPivot.SelectedIndex == 2)
            {
                LoadHistoryUI();
                _autoRefreshTimer.Stop();
            }
            else if (MainPivot.SelectedItem == PivotDocument)
            {
                _autoRefreshTimer.Start();
            }
            else
            {
                _autoRefreshTimer.Stop();
            }
        }

        private void LoadBookmarks()
        {
            BookmarksList.ItemsSource = Bookmarks;
        }

        private async void OnBookmarkSelected(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null) return;

            var bm = button.Tag as GopherBookmark;
            if (bm == null) return;

            MainPivot.SelectedIndex = 0;

            var item = new GopherItem
            {
                Title = bm.Title,
                Host = bm.Host,
                Port = bm.Port,
                Selector = bm.Selector,
                ItemType = bm.ItemType
            };

            await RouteGopherItemAsync(item);
        }

        private void OnDeleteBookmarkClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var bm = button != null ? button.Tag as GopherBookmark : null;
            if (bm == null) return;

            Bookmarks.Remove(bm);
            SaveData();
            LoadBookmarks();
            UpdateAddressSuggestions();
        }

        private void OnClearBookmarksClick(object sender, RoutedEventArgs e)
        {
            Bookmarks.Clear();
            SaveData();
            LoadBookmarks();
            UpdateAddressSuggestions();
        }

        private void LoadHistoryUI()
        {
            HistoryList.ItemsSource = History;
        }

        private async void OnHistorySelected(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null) return;

            var item = button.Tag as GopherHistoryItem;
            if (item == null) return;

            MainPivot.SelectedIndex = 0;

            var simulatedItem = new GopherItem
            {
                Host = item.Host,
                Port = item.Port,
                Selector = item.Selector,
                Title = item.Path,
                ItemType = item.ItemType
            };

            await RouteGopherItemAsync(simulatedItem);
        }

        private void OnDeleteHistoryItemClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var item = button != null ? button.Tag as GopherHistoryItem : null;
            if (item == null) return;

            History.Remove(item);
            SaveData();
            LoadHistoryUI();
            UpdateAddressSuggestions();
        }

        private void OnClearHistoryClick(object sender, RoutedEventArgs e)
        {
            History.Clear();
            SaveData();
            LoadHistoryUI();
            UpdateAddressSuggestions();
        }

        private void OnRefreshClick(object sender, EventArgs e)
        {
            if (MainPivot.SelectedItem == PivotDocument && _currentDocument != null)
            {
                RefreshCurrentDocument();
            }
            else if (MainPivot.SelectedIndex == 0 && !string.IsNullOrEmpty(_currentHost))
            {
                LoadGopherPage(_currentHost, _currentPort, _currentSelector, false, true);
            }
        }

        private void OnHomeClick(object sender, EventArgs e)
        {
            if (MainPivot.SelectedIndex != 0)
            {
                MainPivot.SelectedIndex = 0;
            }

            AddressBox.Text = "";
            string homeHost = "gopher.debene.dev";
            int homePort = 70;
            string homeSelector = "";

            LoadGopherPage(homeHost, homePort, homeSelector);
        }

        private void MainPivot_Loaded(object sender, RoutedEventArgs e)
        {
        }

        #endregion
    }

    public class FormattedTextLine
    {
        public string Text { get; set; }
        public bool IsAsciiArt { get; set; }

        public TextWrapping WrappingMode
        {
            get { return IsAsciiArt ? TextWrapping.NoWrap : TextWrapping.Wrap; }
        }

        public double FontSize
        {
            get { return IsAsciiArt ? 11.0 : 17.0; }
        }

        public double LineHeight
        {
            get { return IsAsciiArt ? 13.0 : 22.0; }
        }
    }

    public class GopherHistoryItem
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public string Selector { get; set; }
        public GopherItemType ItemType { get; set; }

        public string Path
        {
            get
            {
                if (string.IsNullOrEmpty(Selector))
                    return Port == 70 ? Host : Host + ":" + Port;
                return Selector;
            }
        }
    }
}