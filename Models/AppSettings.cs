using System.Collections.Generic;

namespace TelegramProxy.Models
{
    public class TelegramDc
    {
        public string Name { get; set; } = "";
        public string Ip { get; set; } = "";
        public int Port { get; set; } = 443;
    }

    public class AppSettings
    {
        public int LocalPort { get; set; } = 8080;
        public string ActiveDcName { get; set; } = "DC2";
        public string SocksUsername { get; set; } = "admin";
        public string SocksPassword { get; set; } = "TgProxy2026!";
        
        public List<TelegramDc> Datacenters { get; set; } = new()
        {
            new TelegramDc { Name = "DC1", Ip = "149.154.175.50", Port = 443 },
            new TelegramDc { Name = "DC2", Ip = "149.154.167.51", Port = 443 },
            new TelegramDc { Name = "DC3", Ip = "149.154.175.100", Port = 443 },
            new TelegramDc { Name = "DC4", Ip = "149.154.167.91", Port = 443 },
            new TelegramDc { Name = "DC5", Ip = "91.108.56.110", Port = 443 }
        };
    }
}
