using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using MetroGopher.Models;

namespace MetroGopher.Services
{
    public class GopherClient
    {
        public Task<string> RawRequestAsync(string host, int port, string selector)
        {
            var tcs = new TaskCompletionSource<string>();

            try
            {
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                var endpoint = new DnsEndPoint(host, port);

                var connectArgs = new SocketAsyncEventArgs();
                // В WP Silverlight RemoteEndpoint устанавливается напрямую в args
                connectArgs.RemoteEndPoint = endpoint;

                connectArgs.Completed += (s, e) =>
                {
                    if (e.SocketError != SocketError.Success)
                    {
                        tcs.SetException(new Exception("Ошибка подключения: " + e.SocketError));
                        return;
                    }

                    byte[] requestBytes = Encoding.UTF8.GetBytes((selector ?? string.Empty) + "\r\n");
                    var sendArgs = new SocketAsyncEventArgs();
                    sendArgs.SetBuffer(requestBytes, 0, requestBytes.Length);

                    sendArgs.Completed += (sendSocket, sendE) =>
                    {
                        if (sendE.SocketError != SocketError.Success)
                        {
                            tcs.SetException(new Exception("Ошибка отправки: " + sendE.SocketError));
                            return;
                        }

                        var memoryStream = new MemoryStream();
                        ReceiveData(socket, memoryStream, tcs);
                    };

                    if (!socket.SendAsync(sendArgs))
                    {
                        if (sendArgs.SocketError == SocketError.Success)
                        {
                            var memoryStream = new MemoryStream();
                            ReceiveData(socket, memoryStream, tcs);
                        }
                        else
                        {
                            tcs.SetException(new Exception("Ошибка отправки: " + sendArgs.SocketError));
                        }
                    }
                };

                // Вызываем ConnectAsync
                if (!socket.ConnectAsync(connectArgs))
                {
                    if (connectArgs.SocketError != SocketError.Success)
                    {
                        tcs.SetException(new Exception("Ошибка подключения: " + connectArgs.SocketError));
                    }
                }
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }

            return tcs.Task;
        }

        private void ReceiveData(Socket socket, MemoryStream memoryStream, TaskCompletionSource<string> tcs)
        {
            byte[] buffer = new byte[2048];
            var receiveArgs = new SocketAsyncEventArgs();
            receiveArgs.SetBuffer(buffer, 0, buffer.Length);

            EventHandler<SocketAsyncEventArgs> onReceiveCompleted = null;
            onReceiveCompleted = (s, e) =>
            {
                if (e.SocketError == SocketError.Success && e.BytesTransferred > 0)
                {
                    memoryStream.Write(e.Buffer, e.Offset, e.BytesTransferred);

                    if (!socket.ReceiveAsync(receiveArgs))
                    {
                        onReceiveCompleted(socket, receiveArgs);
                    }
                }
                else
                {
                    receiveArgs.Completed -= onReceiveCompleted;
                    socket.Dispose();

                    string result = Encoding.UTF8.GetString(memoryStream.ToArray(), 0, (int)memoryStream.Length);
                    tcs.SetResult(result);
                }
            };

            receiveArgs.Completed += onReceiveCompleted;

            if (!socket.ReceiveAsync(receiveArgs))
            {
                onReceiveCompleted(socket, receiveArgs);
            }
        }

        public List<GopherItem> ParseMenu(string rawResponse, string currentHost, int currentPort)
        {
            var items = new List<GopherItem>();
            if (string.IsNullOrEmpty(rawResponse))
                return items;

            var lines = rawResponse.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            string pendingInfoBlock = "";

            foreach (var line in lines)
            {
                if (line == "." || string.IsNullOrWhiteSpace(line))
                    continue;

                char typeChar = line[0];
                string[] parts = line.Substring(1).Split('\t');

                if (parts.Length >= 4)
                {
                    string title = parts[0];
                    var itemType = MapType(typeChar);

                    // 1. Игнорируем чистый декоративный мусор (разделительные полосы из '=' или '-')
                    string trimmed = title.Trim();
                    if (trimmed.StartsWith("===") || trimmed.StartsWith("---") || trimmed.StartsWith("***"))
                    {
                        continue;
                    }

                    // 2. Если это инфо-строка (i) — накапливаем её в общий блок текста
                    if (itemType == GopherItemType.Info)
                    {
                        if (!string.IsNullOrWhiteSpace(title))
                        {
                            if (pendingInfoBlock.Length > 0)
                                pendingInfoBlock += " ";
                            pendingInfoBlock += title;
                        }
                        continue;
                    }

                    // 3. Если встретили кликабельный элемент (папку/файл), сначала выводим накопленный текст
                    if (!string.IsNullOrEmpty(pendingInfoBlock))
                    {
                        items.Add(new GopherItem
                        {
                            ItemType = GopherItemType.Info,
                            Title = pendingInfoBlock,
                            Host = currentHost,
                            Port = currentPort
                        });
                        pendingInfoBlock = ""; // Сбрасываем буфер
                    }

                    // 4. Корректируем null.host / error.host
                    int itemPort;
                    int.TryParse(parts[3], out itemPort);
                    string itemHost = parts[2];

                    if (string.Equals(itemHost, "null.host", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(itemHost, "error.host", StringComparison.OrdinalIgnoreCase) ||
                        string.IsNullOrWhiteSpace(itemHost))
                    {
                        itemHost = currentHost;
                    }

                    items.Add(new GopherItem
                    {
                        ItemType = itemType,
                        Title = title,
                        Selector = parts[1],
                        Host = itemHost,
                        Port = itemPort == 0 ? currentPort : itemPort
                    });
                }
            }

            // Если в самом конце ответа остался накопленный текст — добавляем его
            if (!string.IsNullOrEmpty(pendingInfoBlock))
            {
                items.Add(new GopherItem
                {
                    ItemType = GopherItemType.Info,
                    Title = pendingInfoBlock,
                    Host = currentHost,
                    Port = currentPort
                });
            }

            return items;
        }

        private GopherItemType MapType(char typeChar)
        {
            switch (typeChar)
            {
                case '0': return GopherItemType.TextFile;
                case '1': return GopherItemType.Directory;
                case '7': return GopherItemType.Search;
                case 'g':
                case 'I':
                case 'p': return GopherItemType.Image;
                case '9': return GopherItemType.Binary;
                case 'i': return GopherItemType.Info; // Информационный текст (не ссылка)
                default: return GopherItemType.Unknown;
            }
        }
    }
}