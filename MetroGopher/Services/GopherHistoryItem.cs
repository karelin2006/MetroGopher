using System;

namespace MetroGopher.Services
{
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