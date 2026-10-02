using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Networking;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace MetroGopher.Services
{
    public class GopherClient
    {
        private const int DefaultTimeoutSeconds = 15;

        /// <summary>
        /// Выполняет текстовый запрос (для меню и текстовых файлов RFC 1436)
        /// </summary>
        public async Task<string> FetchTextAsync(string host, int port, string selector, string searchQuery = null)
        {
            byte[] data = await RawRequestBytesAsync(host, port, selector, searchQuery);
            if (data == null || data.Length == 0)
                return string.Empty;

            // Сначала пробуем декодировать как UTF-8. Если есть невалидные последовательности — берем ISO-8859-1 (Latin1)
            try
            {
                var utf8Strict = new UTF8Encoding(false, true);
                return utf8Strict.GetString(data, 0, data.Length);
            }
            catch (DecoderFallbackException)
            {
                // Fallback на стандартную кодировку классического Gopher (RFC 1436)
                return Encoding.GetEncoding("ISO-8859-1").GetString(data, 0, data.Length);
            }
        }

        /// <summary>
        /// Выполняет сырой запрос байтов (для картинок, бинарников, аудио и меню)
        /// </summary>
        public async Task<byte[]> RawRequestBytesAsync(string host, int port, string selector, string searchQuery = null)
        {
            if (string.IsNullOrWhiteSpace(host))
                throw new ArgumentException("Хост не может быть пустым.");

            if (port <= 0)
                port = 70;

            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(DefaultTimeoutSeconds)))
            using (var socket = new StreamSocket())
            {
                socket.Control.NoDelay = true;

                try
                {
                    await socket.ConnectAsync(new HostName(host), port.ToString()).AsTask(cts.Token);

                    // Если это поиск (Type 7) — отправляется <selector>\t<query>\r\n (RFC 1436, разд. 3.7.2)
                    string request = selector ?? string.Empty;
                    if (!string.IsNullOrEmpty(searchQuery))
                    {
                        request += "\t" + searchQuery;
                    }
                    request += "\r\n";

                    byte[] requestBytes = Encoding.UTF8.GetBytes(request);

                    using (var writer = new DataWriter(socket.OutputStream))
                    {
                        writer.WriteBytes(requestBytes);
                        await writer.StoreAsync().AsTask(cts.Token);
                        await writer.FlushAsync().AsTask(cts.Token);
                        writer.DetachStream();
                    }

                    using (var reader = new DataReader(socket.InputStream))
                    {
                        reader.InputStreamOptions = InputStreamOptions.Partial;
                        using (var ms = new MemoryStream())
                        {
                            while (true)
                            {
                                uint loaded = await reader.LoadAsync(64 * 1024).AsTask(cts.Token);
                                if (loaded == 0)
                                    break;

                                byte[] chunk = new byte[loaded];
                                reader.ReadBytes(chunk);
                                ms.Write(chunk, 0, (int)loaded);
                            }
                            return ms.ToArray();
                        }
                    }
                }
                catch (TaskCanceledException)
                {
                    throw new TimeoutException($"Сервер {host}:{port} не ответил вовремя.");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Сетевая ошибка ({host}): {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Парсинг директории (Type 1) согласно RFC 1436
        /// </summary>
        public List<GopherItem> ParseMenu(string rawResponse, string currentHost, int currentPort)
        {
            var items = new List<GopherItem>();
            if (string.IsNullOrEmpty(rawResponse))
                return items;

            var lines = rawResponse.Replace("\r\n", "\n").Replace('\r', '\n')
                                   .Split(new[] { '\n' }, StringSplitOptions.None);

            GopherItem lastNonRedundantItem = null;

            foreach (var rawLine in lines)
            {
                if (string.IsNullOrEmpty(rawLine))
                    continue;

                // Точка на отдельной строке — маркер конца меню в RFC 1436
                if (rawLine == ".")
                    break;

                char typeChar = rawLine[0];
                string rest = rawLine.Length > 1 ? rawLine.Substring(1) : string.Empty;
                string[] parts = rest.Split('\t');

                string title = parts.Length > 0 ? parts[0] : string.Empty;
                string selector = parts.Length > 1 ? parts[1] : string.Empty;
                string host = parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]) ? parts[2] : currentHost;

                int port = currentPort;
                if (parts.Length > 3)
                {
                    int parsedPort;
                    if (int.TryParse(parts[3], out parsedPort) && parsedPort > 0)
                    {
                        port = parsedPort;
                    }
                }

                // Защита от заглушек серверов
                if (host.Equals("null.host", StringComparison.OrdinalIgnoreCase) ||
                    host.Equals("error.host", StringComparison.OrdinalIgnoreCase))
                {
                    host = currentHost;
                }

                var itemType = MapType(typeChar);

                // Обработка веб-ссылок (URL:...)
                if (itemType == GopherItemType.HtmlLink)
                {
                    if (selector.StartsWith("URL:", StringComparison.OrdinalIgnoreCase))
                    {
                        selector = selector.Substring(4);
                    }
                }

                // Обработка типа '+' (Redundant server - RFC 1436, разд. 3.8)
                if (typeChar == '+' && lastNonRedundantItem != null)
                {
                    itemType = lastNonRedundantItem.ItemType;
                    if (string.IsNullOrEmpty(selector))
                        selector = lastNonRedundantItem.Selector;
                }

                var item = new GopherItem
                {
                    ItemType = itemType,
                    Title = title,
                    Selector = selector,
                    Host = host,
                    Port = port
                };

                items.Add(item);

                if (typeChar != '+')
                {
                    lastNonRedundantItem = item;
                }
            }

            return items;
        }

        /// <summary>
        /// Очистка текста документа (разэкранирование точек и удаление Lastline)
        /// </summary>
        public string CleanTextContent(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return string.Empty;

            var lines = raw.Replace("\r\n", "\n").Replace('\r', '\n')
                           .Split(new[] { '\n' }, StringSplitOptions.None);

            var sb = new StringBuilder();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                // Последняя строка с точкой означает завершение TextFile Entity (RFC 1436, стр. 26)
                if (line == "." && i == lines.Length - 1)
                    break;

                // Согласно RFC 1436: если строка начинается с точки, сервер удваивает её (.. -> .)
                if (line.StartsWith(".."))
                {
                    line = line.Substring(1);
                }

                sb.AppendLine(line);
            }

            return sb.ToString().TrimEnd('\r', '\n');
        }

        private GopherItemType MapType(char typeChar)
        {
            switch (typeChar)
            {
                // RFC 1436 базовые типы
                case '0': return GopherItemType.TextFile;
                case '1': return GopherItemType.Directory;
                case '2': return GopherItemType.CSOPhone;
                case '3': return GopherItemType.Error;
                case '4': return GopherItemType.BinHex;
                case '5': return GopherItemType.DosBinary;
                case '6': return GopherItemType.Uuencoded;
                case '7': return GopherItemType.Search;
                case '8': return GopherItemType.Telnet;
                case '9': return GopherItemType.Binary;
                case '+': return GopherItemType.Redundant;
                case 'T': return GopherItemType.Tn3270;

                // Расширения изображений
                case 'g':
                case 'I':
                case 'p':
                case ':':
                    return GopherItemType.Image;

                // Аудио
                case 's':
                case 'S':
                case '<':
                    return GopherItemType.Audio;

                // Видео
                case ';':
                    return GopherItemType.Video;

                // Документы и архивы
                case 'd':
                case 'D':
                case 'P': // PDF
                case 'M':
                    return GopherItemType.Document;

                // HTML и ссылки
                case 'h':
                case 'H':
                    return GopherItemType.HtmlLink;

                // Информационный текст
                case 'i':
                    return GopherItemType.Info;

                default:
                    return GopherItemType.Unknown;
            }
        }
    }
}