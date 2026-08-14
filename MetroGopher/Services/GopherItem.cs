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
        Image,      // 'g', 'I'
        Info,       // 'i'
        HtmlLink,   // 'h', 'H'
        Unknown
    }

    public class GopherItem
    {
        public GopherItemType ItemType { get; set; }
        public string Title { get; set; }
        public string Selector { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }

        public bool IsClickable => ItemType != GopherItemType.Info && ItemType != GopherItemType.Unknown;

        public bool IsInfo => ItemType == GopherItemType.Info || ItemType == GopherItemType.Unknown;

        public string Symbol
        {
            get
            {
                switch (ItemType)
                {
                    case GopherItemType.Directory: return "\uE14C";
                    case GopherItemType.TextFile: return "\uE160";
                    case GopherItemType.Search: return "\uE11A";
                    case GopherItemType.Image: return "\uE114";
                    case GopherItemType.Binary: return "\uE125";
                    case GopherItemType.HtmlLink: return "\uE128";
                    default: return "\uE128";
                }
            }
        }
    }
}