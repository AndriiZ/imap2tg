using System.Text.RegularExpressions;

namespace ImapTelegramNotifier
{
    public static class TextMatcher
    {
        public static bool ShouldIgnore(string rawText, string[] ignorePatterns)
        {
            if (string.IsNullOrEmpty(rawText) || ignorePatterns == null || ignorePatterns.Length == 0)
            {
                return false;
            }

            return ignorePatterns.Any(pattern => MatchesPattern(rawText, pattern));
        }

        private static bool MatchesPattern(string text, string pattern)
        {
            var simpleCompare = text.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;
            if (simpleCompare)
                return true;

            try
            {
                // Try to use the pattern as a regex
                var regex = new Regex(pattern, RegexOptions.IgnoreCase);
                return regex.IsMatch(text);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }
}
