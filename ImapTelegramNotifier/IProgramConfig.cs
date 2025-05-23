namespace ImapTelegramNotifier
{
    internal class IProgramConfig
    {
        // Configuration properties
        public string? imapServer { get; set; }
        public int imapPort { get; set; }
        public string? emailUsername { get; set; }
        public string? emailPassword { get; set; }
        public string? mailboxToMonitor { get; set; }
        public bool useSSL { get; set; }
        public string? telegramBotToken { get; set; }
        public long telegramChatId { get; set; }
        public Template? template { get; set; }
        public int previewLength { get; set; } = 1024;
        public bool sendRawTextAsAttachment { get; set; } = true;
    }
}