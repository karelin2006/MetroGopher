using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.IsolatedStorage;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using Microsoft.Phone.Controls;
using MetroGopher.Services;

namespace MetroGopher
{
    public partial class MainPage : PhoneApplicationPage
    {
        private readonly GopherClient _client = new GopherClient();

        // Временная история ТОЛЬКО для кнопки "Назад"
        private readonly Stack<GopherHistoryItem> _historyStack = new Stack<GopherHistoryItem>();
        private bool _isNavigatingHistory = false;

        private string _currentHost = "gopher.debene.dev";
        private int _currentPort = 70;
        private string _currentSelector = "";

        private GopherItem _currentDocument = null;

        private readonly System.Windows.Threading.DispatcherTimer _autoRefreshTimer = new System.Windows.Threading.DispatcherTimer();

        private readonly string[] _knownGopherServers =
        {
            "gopher.floodgap.com", "sdf.org", "gopher.viste.fr",
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

        private void DisplayLongText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                DocumentListBox.ItemsSource = null;
                return;
            }

            var blocks = new List<string>();
            var sb = new StringBuilder();
            int lineCount = 0;

            var lines = text.Split(new[] { '\n' }, StringSplitOptions.None);
            foreach (var line in lines)
            {
                sb.AppendLine(line);
                lineCount++;

                if (lineCount >= 40)
                {
                    blocks.Add(sb.ToString().TrimEnd('\r', '\n'));
                    sb.Clear();
                    lineCount = 0;
                }
            }

            if (sb.Length > 0)
            {
                blocks.Add(sb.ToString().TrimEnd('\r', '\n'));
            }

            DocumentListBox.ItemsSource = blocks;
            MainPivot.SelectedItem = PivotDocument;
        }

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

                            // Читаем тип (с защитой от старых записей без типа)
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

            // Сохраняем историю вместе с типом файла
            settings[HistoryKey] = new List<string>(
                History.Select(h => h.Host + "|" + h.Port + "|" + h.Selector + "|" + h.ItemType));

            settings.Save();
        }

        // НОВЫЙ МЕТОД: Универсальное сохранение любой страницы в историю
        private void SaveToPermanentHistory(string host, int port, string selector, GopherItemType type)
        {
            if (string.IsNullOrEmpty(host) || _isNavigatingHistory) return;

            string fullAddress = port == 70 ? host + selector : host + ":" + port + selector;

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

            if (string.IsNullOrEmpty(selector))
                AddressBox.Text = port == 70 ? host : host + ":" + port;
            else
                AddressBox.Text = port == 70 ? host + selector : host + ":" + port + selector;

            if (addToHistory && !_isNavigatingHistory && !string.IsNullOrEmpty(host))
            {
                _historyStack.Push(new GopherHistoryItem { Host = host, Port = port, Selector = selector, ItemType = GopherItemType.Directory });

                // Вызываем наш новый универсальный метод
                SaveToPermanentHistory(host, port, selector, GopherItemType.Directory);
            }

            try
            {
                string rawData = await _client.RawRequestAsync(host, port, selector, forceRefresh);
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

        private async void OnItemSelected(object sender, SelectionChangedEventArgs e)
        {
            var selectedItem = GopherList.SelectedItem as GopherItem;
            if (selectedItem == null) return;

            GopherList.SelectedItem = null;

            switch (selectedItem.ItemType)
            {
                case GopherItemType.Directory:
                    LoadGopherPage(selectedItem.Host, selectedItem.Port, selectedItem.Selector);
                    break;

                case GopherItemType.TextFile:
                    await OpenTextFileAsync(selectedItem);
                    break;

                case GopherItemType.Search:
                    ShowSearchDialog(selectedItem);
                    break;

                case GopherItemType.Info:
                    break;

                case GopherItemType.Error:
                    MessageBox.Show(selectedItem.Title ?? "Error", "Server Error", MessageBoxButton.OK);
                    break;

                case GopherItemType.Binary:
                case GopherItemType.DosBinary:
                case GopherItemType.BinHex:
                case GopherItemType.Uuencoded:
                case GopherItemType.Image:
                    await DownloadAndSaveFileAsync(selectedItem);
                    break;

                case GopherItemType.Unknown:
                default:
                    try
                    {
                        await OpenTextFileAsync(selectedItem);
                    }
                    catch
                    {
                        var result = MessageBox.Show(
                            "Unknown resource type.\nDownload as file?",
                            selectedItem.Title ?? "File",
                            MessageBoxButton.OKCancel);

                        if (result == MessageBoxResult.OK)
                            await DownloadAndSaveFileAsync(selectedItem);
                    }
                    break;
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
                Caption = "GOPHER SEARCH",
                Message = string.IsNullOrEmpty(selectedItem.Title) ? "Enter query:" : selectedItem.Title,
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

        private async Task OpenTextFileAsync(GopherItem item)
        {
            LoadingBar.Visibility = Visibility.Visible;
            DocumentListBox.ItemsSource = new List<string> { "Loading..." };

            // Документ также добавляется в историю!
            SaveToPermanentHistory(item.Host, item.Port, item.Selector, item.ItemType);

            try
            {
                string raw = await _client.RawRequestAsync(item.Host, item.Port, item.Selector);
                string clean = _client.CleanTextContent(raw);

                if (IsProbablyBinary(clean))
                {
                    var res = MessageBox.Show(
                        "This does not look like a text file.\nDownload as binary?",
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
                DocumentListBox.ItemsSource = new List<string> { "Load failed." };
                MessageBox.Show("Failed to open file:\n" + ex.Message, "Error", MessageBoxButton.OK);
            }
            finally
            {
                LoadingBar.Visibility = Visibility.Collapsed;
            }
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

        private async Task DownloadAndSaveFileAsync(GopherItem item)
        {
            LoadingBar.Visibility = Visibility.Visible;

            // Скачивания файлов тоже добавляются в историю!
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
                        fullPath = "Downloads\\" + nameWithoutExt + "_" + counter + ext;
                        counter++;
                    }

                    using (var stream = store.CreateFile(fullPath))
                    {
                        stream.Write(data, 0, data.Length);
                    }
                }

                MessageBox.Show(
                    "File saved:\n" + fileName + "\n\nSize: " + FormatSize(data.Length),
                    "Download Complete",
                    MessageBoxButton.OK);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Download error:\n" + ex.Message, "Error", MessageBoxButton.OK);
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
                    case GopherItemType.Binary:
                    case GopherItemType.DosBinary: ext = ".bin"; break;
                    case GopherItemType.BinHex: ext = ".hqx"; break;
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
            DocumentListBox.ItemsSource = new List<string> { "Loading..." };

            try
            {
                string raw = await _client.RawRequestAsync(_currentDocument.Host, _currentDocument.Port, _currentDocument.Selector, true);
                string clean = _client.CleanTextContent(raw);

                if (IsProbablyBinary(clean))
                {
                    var res = MessageBox.Show(
                        "This does not look like a text file.\nDownload as binary?",
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
                DocumentListBox.ItemsSource = new List<string> { "Load failed." };
                MessageBox.Show("Failed to refresh document:\n" + ex.Message, "Error", MessageBoxButton.OK);
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
                string raw = await _client.RawRequestAsync(_currentDocument.Host, _currentDocument.Port, _currentDocument.Selector, true);
                string clean = _client.CleanTextContent(raw);

                if (!IsProbablyBinary(clean))
                {
                    DisplayLongText(clean);
                }
            }
            catch
            {
                // Тихо игнорируем сетевые сбои
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

            MessageBox.Show("Page added to bookmarks!", "Bookmarks", MessageBoxButton.OK);
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

            switch (bm.ItemType)
            {
                case GopherItemType.TextFile:
                    var textItem = new GopherItem
                    {
                        Title = bm.Title,
                        Host = bm.Host,
                        Port = bm.Port,
                        Selector = bm.Selector,
                        ItemType = GopherItemType.TextFile
                    };
                    await OpenTextFileAsync(textItem);
                    break;

                case GopherItemType.Binary:
                case GopherItemType.DosBinary:
                case GopherItemType.BinHex:
                case GopherItemType.Uuencoded:
                case GopherItemType.Image:
                    var binItem = new GopherItem
                    {
                        Title = bm.Title,
                        Host = bm.Host,
                        Port = bm.Port,
                        Selector = bm.Selector,
                        ItemType = bm.ItemType
                    };
                    await DownloadAndSaveFileAsync(binItem);
                    break;

                default:
                    LoadGopherPage(bm.Host, bm.Port, bm.Selector);
                    break;
            }
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

        // ОБНОВЛЕННЫЙ МЕТОД: Умеет открывать и папки, и документы из истории
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

            switch (simulatedItem.ItemType)
            {
                case GopherItemType.Directory:
                    LoadGopherPage(simulatedItem.Host, simulatedItem.Port, simulatedItem.Selector);
                    break;

                case GopherItemType.TextFile:
                    await OpenTextFileAsync(simulatedItem);
                    break;

                case GopherItemType.Search:
                    ShowSearchDialog(simulatedItem);
                    break;

                case GopherItemType.Binary:
                case GopherItemType.DosBinary:
                case GopherItemType.BinHex:
                case GopherItemType.Uuencoded:
                case GopherItemType.Image:
                    await DownloadAndSaveFileAsync(simulatedItem);
                    break;

                case GopherItemType.Unknown:
                default:
                    try
                    {
                        await OpenTextFileAsync(simulatedItem);
                    }
                    catch
                    {
                        var result = MessageBox.Show(
                            "Unknown resource type.\nDownload as file?",
                            simulatedItem.Title ?? "File",
                            MessageBoxButton.OKCancel);

                        if (result == MessageBoxResult.OK)
                            await DownloadAndSaveFileAsync(simulatedItem);
                    }
                    break;
            }
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
    }

    public class GopherHistoryItem
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public string Selector { get; set; }
        public GopherItemType ItemType { get; set; } // Добавили тип для истории!

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