namespace ImapTelegramNotifier
{
    internal static class ProgramHelpers
    {

        internal static void Log(string message)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            Console.WriteLine($"[{timestamp}] {message}");
        }

        internal static bool CreateDirectorySafely(string path, int maxRetries = 3, int delayMilliseconds = 100)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Directory path cannot be null or empty", nameof(path));
            }

            // If directory already exists, return immediately
            if (Directory.Exists(path))
            {
                return true;
            }

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    // Another process might have created the directory between our check and create
                    Directory.CreateDirectory(path);
                    return true;
                }
                catch (IOException ex) when (attempt < maxRetries)
                {
                    // Log the exception if you have a logging framework
                    Console.WriteLine($"Attempt {attempt} failed to create directory '{path}': {ex.Message}");
                    Thread.Sleep(delayMilliseconds * attempt); // Exponential backoff
                }
                catch (UnauthorizedAccessException ex) when (attempt < maxRetries)
                {
                    Console.WriteLine($"Permission denied creating directory '{path}': {ex.Message}");
                    Thread.Sleep(delayMilliseconds * attempt);
                }
                catch (Exception ex) when (attempt < maxRetries)
                {
                    Console.WriteLine($"Unexpected error creating directory '{path}': {ex.Message}");
                    Thread.Sleep(delayMilliseconds * attempt);
                }
            }

            // Final attempt outside the loop to let any exceptions propagate
            try
            {
                Directory.CreateDirectory(path);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to create directory '{path}' after {maxRetries} attempts: {ex.Message}");
                return false;
            }
        }
    }
}