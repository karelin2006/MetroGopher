using System;

namespace MetroGopher.Models
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
        Image,      // 'g', 'I'
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

        // Проверяем, кликабельный ли элемент
        public bool IsClickable => ItemType != GopherItemType.Info && ItemType != GopherItemType.Unknown;

        // Проверяем, просто ли это инфо-текст
        public bool IsInfo => ItemType == GopherItemType.Info || ItemType == GopherItemType.Unknown;

        public string Symbol
        {
            get
            {
                switch (ItemType)
                {
                    case GopherItemType.Directory: return "\uE14C"; // Папка
                    case GopherItemType.TextFile: return "\uE160"; // Документ
                    case GopherItemType.Search: return "\uE11A"; // Поиск
                    case GopherItemType.Image: return "\uE114"; // Картинка
                    case GopherItemType.Binary: return "\uE125"; // Файл
                    default: return "\uE128"; // Веб-ссылка
                }
            }
        }
    }
}