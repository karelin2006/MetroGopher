using System;

namespace MetroGopher.Services
{
    public enum GopherItemType
    {
        TextFile,   // '0'
        Directory,  // '1'
        CSOPhone,   // '2'
        Error,      // '3'
        BinHex,     // '4'
        DosBinary,  // '5'
        Uuencoded,  // '6'
        Search,     // '7'
        Telnet,     // '8'
        Binary,     // '9'
        Redundant,  // '+' (дублирующий сервер)
        Tn3270,     // 'T'
        Image,      // 'g', 'I', ':', 'p', 'P'
        Audio,      // 's', 'S', '<'
        Video,      // ';'
        Document,   // 'd', 'D', 'M'
        HtmlLink,   // 'h', 'H'
        Info,       // 'i'
        Unknown
    }

    public class GopherItem
    {
        public GopherItemType ItemType { get; set; }
        public string Title { get; set; }
        public string Selector { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }

        // Элемент кликабелен, если это не инфо-строка и не ошибка сервера
        public bool IsClickable => ItemType != GopherItemType.Info &&
                                   ItemType != GopherItemType.Error &&
                                   ItemType != GopherItemType.Unknown;

        public bool IsInfo => ItemType == GopherItemType.Info;
        public bool IsError => ItemType == GopherItemType.Error;

        // Корректные символы шрифта Segoe UI Symbol (стандарт Windows Phone 8.1)
        public string Symbol
        {
            get
            {
                switch (ItemType)
                {
                    case GopherItemType.Directory: return "\uE188"; // Папка
                    case GopherItemType.TextFile: return "\uE160"; // Документ
                    case GopherItemType.Search: return "\uE11A"; // Лупа
                    case GopherItemType.Image: return "\uE114"; // Картинка
                    case GopherItemType.Audio: return "\uE189"; // Звук / медиа
                    case GopherItemType.Video: return "\uE116"; // Видео
                    case GopherItemType.HtmlLink: return "\uE12B"; // Глобус / Веб-ссылка
                    case GopherItemType.Binary:
                    case GopherItemType.DosBinary:
                    case GopherItemType.BinHex:
                    case GopherItemType.Uuencoded:
                    case GopherItemType.Document: return "\uE118"; // Загрузка / файл
                    case GopherItemType.Telnet:
                    case GopherItemType.Tn3270: return "\uE1D1"; // Терминал / консоль
                    case GopherItemType.CSOPhone: return "\uE13A"; // Телефонная книга
                    case GopherItemType.Error: return "\uE10A"; // Предупреждение / Ошибка
                    default: return string.Empty;
                }
            }
        }

        public class FormattedTextLine
        {
            public string Text { get; set; }
            public bool IsAsciiArt { get; set; }
        }
    }

}
