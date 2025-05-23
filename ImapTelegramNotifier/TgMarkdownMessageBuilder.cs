using System.Text;
using MimeKit;
using static System.Net.Mime.MediaTypeNames;

namespace ImapTelegramNotifier
{
    internal class TgMarkdownMessageBuilder : IMessageBuilder
    {
        private readonly Template? template;
        private readonly int previewLength;

        public TgMarkdownMessageBuilder(Template? template, int previewLength = 1024)
        {
            this.template = template;
            this.previewLength = previewLength;
        }

        public Func<string?, string?> Escape(Telegram.Bot.Types.Enums.ParseMode parseMode)
        {
            switch (parseMode)
            {
                case Telegram.Bot.Types.Enums.ParseMode.MarkdownV2:
                    return EscapeMarkdownV2;
                default:
                    return (m) => m;
            }
        }

        public (StringBuilder preview, string rawText) BuildMessage(MimeMessage message, Telegram.Bot.Types.Enums.ParseMode parseMode)
        {
            var sender = message.From.ToString();
            var subject = message.Subject ?? "(No subject)";
            var body = GetBody(message);
            var preview = body;
            if (body.Length > previewLength)
            {
                preview = preview.Substring(0, previewLength) + "...";
            }
            var escapeFunc = Escape(parseMode);

            // Create notification using template or default format
            StringBuilder notification = new StringBuilder();

            var replacements = new Dictionary<string, Func<string, string>>
                { 
                    { "{{sender}}", s => s.Replace("{{sender}}", sender) },
                    { "{{subject}}", s => s.Replace("{{subject}}", subject) },
                    { "{{preview}}", s => s.Replace("{{preview}}", preview) },
                    { "{{date}}", s => s.Replace("{{date}}", DateTime.Now.ToString()) }
            };

            var escapeFunctions = new Dictionary<string, Func<string?, string?>>
            {
                { "*", EscapeMarkdownV2 }
            };

            if (template != null)
            {
                // Replace placeholders in each template part
                if (template.Header != null)
                {
                    var header = TemplateProcessor.EvaluateTemplate(template.Header, message, replacements, escapeFunctions);
                    notification.AppendLine(header);
                }
                if (template.Subheader != null)
                {
                    var subheader = TemplateProcessor.EvaluateTemplate(template.Subheader, message, replacements, escapeFunctions);
                    notification.AppendLine(subheader);
                }
                if (template.Body != null)
                {
                    var messageBody = TemplateProcessor.EvaluateTemplate(template.Body, message, replacements, escapeFunctions);
                    notification.AppendLine(messageBody);
                }
                if (template.Footer != null)
                {
                    var footer = TemplateProcessor.EvaluateTemplate(template.Footer, message, replacements, escapeFunctions);
                    notification.AppendLine(footer);
                }
                return (preview: notification, rawText: body);
            }

            // Fallback to default template
            notification.AppendLine("📧 *New Email*");
            notification.AppendLine($"*From:* {escapeFunc(sender)}");
            notification.AppendLine($"*Subject:* {escapeFunc(subject)}");
            if (!string.IsNullOrEmpty(preview))
            {
                notification.AppendLine($"*Preview:* {escapeFunc(preview)}");
            }
            return (preview: notification, rawText: body);
        }

        private static string GetBody(MimeMessage message)
        {
            string text = string.Empty;

            if (message.TextBody != null)
            {
                text = message.TextBody;
            }
            else if (message.HtmlBody != null)
            {
                text = System.Net.WebUtility.HtmlDecode(message.HtmlBody);
            }

            text = text.Trim();
            return text;
        }
        private static string? EscapeMarkdownV2(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            // Characters that need to be escaped in MarkdownV2: _ * [ ] ( ) ~ ` > # + - = | { } . !
            return text
                .Replace("_", "\\_")
                .Replace("*", "\\*")
                .Replace("[", "\\[")
                .Replace("]", "\\]")
                .Replace("(", "\\(")
                .Replace(")", "\\)")
                .Replace("~", "\\~")
                .Replace("`", "\\`")
                .Replace(">", "\\>")
                .Replace("#", "\\#")
                .Replace("+", "\\+")
                .Replace("-", "\\-")
                .Replace("=", "\\=")
                .Replace("|", "\\|")
                .Replace("{", "\\{")
                .Replace("}", "\\}")
                .Replace(".", "\\.")
                .Replace("!", "\\!");
        }
    }
}
