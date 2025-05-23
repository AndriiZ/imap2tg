using System.Text;
using MimeKit;
using Telegram.Bot.Types.Enums;

namespace ImapTelegramNotifier
{
    internal interface IMessageBuilder
    {
        (StringBuilder preview, string rawText) BuildMessage(MimeMessage message, Telegram.Bot.Types.Enums.ParseMode parseMode);
    }
}