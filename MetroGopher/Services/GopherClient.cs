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
        public async Task<string> RawRequestAsync(string host, int port, string selector, bool forceRefresh = false)
        {
            byte[] data = await RawRequestBytesAsync(host, port, selector);
            if (data == null || data.Length == 0)
                return string.Empty;

            return Encoding.UTF8.GetString(data, 0, data.Length);
        }

        public async Task<byte[]> RawRequestBytesAsync(string host, int port, string selector)
        {
            if (string.IsNullOrWhiteSpace(host))
                throw new ArgumentException("Host is empty");

            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            using (var socket = new StreamSocket())
            {
                socket.Control.NoDelay = true;

                try
                {
                    await socket.ConnectAsync(new HostName(host), port.ToString()).AsTask(cts.Token);

                    string request = (selector ?? string.Empty) + "\r\n";
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
                        var ms = new MemoryStream();

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
                catch (TaskCanceledException)
                {
                    throw new TimeoutException($"Сервер {host} не ответил за 15 секунд. Возможно, он отключен.");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Ошибка сети ({host}): {ex.Message}");
                }
            }
        }

        public List<GopherItem> ParseMenu(string rawResponse, string currentHost, int currentPort)
        {
            var items = new List<GopherItem>();
            if (string.IsNullOrEmpty(rawResponse))
                return items;

            var lines = rawResponse.Replace("\r\n", "\n").Replace('\r', '\n')
                                   .Split(new[] { '\n' }, StringSplitOptions.None);

            string pendingInfo = null;

            foreach (var rawLine in lines)
            {
                string line = rawLine;
                if (string.IsNullOrEmpty(line) || line == ".")
                    continue;

                if (line.Length < 1)
                    continue;

                char typeChar = line[0];
                string rest = line.Length > 1 ? line.Substring(1) : string.Empty;
                string[] parts = rest.Split('\t');

                string title = parts.Length > 0 ? parts[0] : string.Empty;
                string selector = parts.Length > 1 ? parts[1] : string.Empty;
                string host = parts.Length > 2 ? parts[2] : currentHost;
                int port = currentPort;

                if (parts.Length > 3)
                {
                    int parsedPort;
                    if (int.TryParse(parts[3], out parsedPort) && parsedPort > 0)
                        port = parsedPort;
                }

                var itemType = MapType(typeChar);

                if (itemType == GopherItemType.HtmlLink)
                {
                    if (selector.StartsWith("URL:", StringComparison.OrdinalIgnoreCase))
                    {
                        selector = selector.Substring(4);
                    }
                }

                if (itemType == GopherItemType.Info)
                {
                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        if (pendingInfo == null)
                            pendingInfo = title;
                        else
                            pendingInfo += "\n" + title;
                    }
                    continue;
                }

                if (pendingInfo != null)
                {
                    items.Add(new GopherItem
                    {
                        ItemType = GopherItemType.Info,
                        Title = pendingInfo,
                        Host = currentHost,
                        Port = currentPort,
                        Selector = ""
                    });
                    pendingInfo = null;
                }

                if (string.IsNullOrWhiteSpace(host) ||
                    host.Equals("null.host", StringComparison.OrdinalIgnoreCase) ||
                    host.Equals("error.host", StringComparison.OrdinalIgnoreCase))
                {
                    host = currentHost;
                }

                if (port <= 0)
                    port = currentPort;

                items.Add(new GopherItem
                {
                    ItemType = itemType,
                    Title = title,
                    Selector = selector,
                    Host = host,
                    Port = port
                });
            }

            if (pendingInfo != null)
            {
                items.Add(new GopherItem
                {
                    ItemType = GopherItemType.Info,
                    Title = pendingInfo,
                    Host = currentHost,
                    Port = currentPort,
                    Selector = ""
                });
            }

            return items;
        }

        public string CleanTextContent(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return raw;

            // Нормализуем переносы строк для Windows Phone
            string cleaned = raw.Replace("\r\n", "\n").Replace('\r', '\n');

            cleaned = cleaned.TrimEnd();
            if (cleaned.EndsWith("\n."))
                cleaned = cleaned.Substring(0, cleaned.Length - 2);
            else if (cleaned.EndsWith("."))
                cleaned = cleaned.Substring(0, cleaned.Length - 1);

            return cleaned;
        }

        private GopherItemType MapType(char typeChar)
        {
            switch (typeChar)
            {
                // Базовые типы RFC 1436
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

                // Зеркальные серверы
                case '+': return GopherItemType.Directory;
                // Терминальные сессии (tn3270)
                case 'T': return GopherItemType.Telnet;

                // Изображения (Gopher+)
                case 'g': // GIF
                case 'I': // Изображение любого типа
                case 'p': // PNG или PDF (в зависимости от сервера)
                case 'P': // Альтернативный маркер PNG/PDF
                case ':': // Bitmap-картинки
                    return GopherItemType.Image;

                // Аудио форматы (.wav, .au, .mp3, .ogg)
                case 's':
                case 'S':
                case '<':
                    return GopherItemType.Binary; // Скачиваем аудио как файл

                // Видео форматы (Movie)
                case ';':
                    return GopherItemType.Binary; // Скачиваем видео как файл

                // Документы (Word, RTF, загружаемые PDF) и архивы MIME
                case 'd':
                case 'D':
                case 'M': // MIME multipart
                    return GopherItemType.Binary; // Безопаснее всего просто скачать

                // Веб-ссылки и HTML-документы
                case 'h':
                case 'H':
                    return GopherItemType.HtmlLink;

                // Информационные строки
                case 'i': return GopherItemType.Info;

                // Структурированные текстовые данные (XML и т.д.)
                case 'x': return GopherItemType.TextFile; // Показываем как текст

                default: return GopherItemType.Unknown;
            }
        }
    }
}