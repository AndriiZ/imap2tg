using System.Text;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using MimeKit;
using Telegram.Bot;
using Microsoft.Extensions.Configuration;
using System.CommandLine;
using Microsoft.Extensions.Primitives;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types;

namespace ImapTelegramNotifier
{

    class Program
    {
        private static IMessageBuilder? messageBuilder;

        // Token for cancellation
        private static CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private static string appName = "imap-telegram-notifier";

        static async Task Main(string[] args)
        {
            var configOption = new Option<string?>(
                        name: "--config",
                        description: "Configuration name.");

            var rootCommand = new RootCommand("Imap Telegram nofifier");
            rootCommand.AddOption(configOption);

            rootCommand.SetHandler(async (config) =>
            {
                await Process(config!);
            },
            configOption);

            await rootCommand.InvokeAsync(args);
        }

        private static StreamWriter? _logWriter;
        private static IConfigurationRoot? config;
        private static IProgramConfig? programConfig;
        private static async Task Process(string? configName = null)
        {
            configName = configName ?? nameof(ImapTelegramNotifier).ToLowerInvariant();
            // Setup logging
            string logDirectory = $"/var/log/{appName}";

            // When running as a normal user for testing, use a different log path
            if (!IsRunningAsService())
            {
                logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            }

            ProgramHelpers.CreateDirectorySafely(logDirectory);
            string logFile = Path.Combine(logDirectory, $"{configName}.log");

            // Redirect console output to log file when running as a service
            if (IsRunningAsService())
            {
                var fileStream = new FileStream(logFile, FileMode.Append, FileAccess.Write, FileShare.Read);
                _logWriter = new StreamWriter(fileStream) { AutoFlush = true };
                Console.SetOut(_logWriter);
                Console.SetError(_logWriter);
            }

            // Load configuration
            config = LoadConfiguration(configName);
            programConfig = BuildConfiguration(config);
            ProgramHelpers.Log("Ignore Patterns: ");
            foreach (var ignorePattern in programConfig.ignorePatterns)
            {
                ProgramHelpers.Log(ignorePattern);
            }
            messageBuilder = new TgMarkdownMessageBuilder(programConfig.template, programConfig.previewLength);

            // Handle Ctrl+C to gracefully exit
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                cancellationTokenSource.Cancel();
                ProgramHelpers.Log("Shutting down...");
            };

            // Handle termination signals for Linux
            AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
            {
                cancellationTokenSource.Cancel();
                ProgramHelpers.Log("Process exit signal received. Shutting down...");
                // Give some time for clean shutdown
                Thread.Sleep(1000);
            };

            try
            {
                await MonitorEmailsAsync(cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                ProgramHelpers.Log("Operation was cancelled.");
            }
            catch (Exception ex)
            {
                ProgramHelpers.Log($"Critical error: {ex.Message}");
                ProgramHelpers.Log(ex?.StackTrace ?? "No stack trace");
                // Exit with error code for systemd
                Environment.Exit(1);
            }
            finally
            {
                _logWriter?.Flush();
                _logWriter?.Dispose();
                ProgramHelpers.Log("Service stopped.");
            }
        }

        private static bool IsRunningAsService()
        {
            // Check if running as a systemd service
            return Environment.GetEnvironmentVariable("DOTNET_RUNNING_AS_SERVICE") == "true";
        }

        private static async Task MonitorEmailsAsync(CancellationToken cancellationToken)
        {
            if (programConfig is null)
            {
                throw new ArgumentNullException(nameof(programConfig));
            }
            using var client = new ImapClient();
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    ProgramHelpers.Log($"Connecting to {programConfig.imapServer}:{programConfig.imapPort}...");
                    // Connect to the IMAP server
                    await client.ConnectAsync(programConfig.imapServer, programConfig.imapPort,
programConfig.useSSL ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls,
                        cancellationToken);
                    ProgramHelpers.Log($"Authenticating {programConfig.emailUsername}...");
                    // Authenticate with the server
                    await client.AuthenticateAsync(programConfig.emailUsername, programConfig.emailPassword, cancellationToken);
                    ProgramHelpers.Log("Connected and authenticated successfully.");

                    // Open the mailbox
                    var inbox = await client.GetFolderAsync(programConfig.mailboxToMonitor, cancellationToken);
                    await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
                    ProgramHelpers.Log($"Monitoring '{programConfig.mailboxToMonitor}' for new messages...");

                    // Keep track of the current message count
                    int initialMessageCount = inbox.Count;
                    ProgramHelpers.Log($"Current message count: {initialMessageCount}");

                    // Start the IDLE mode and keep it running
                    while (client.IsConnected && !cancellationToken.IsCancellationRequested)
                    {
                        using var doneCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                        EventHandler<EventArgs> countChangedHandler = (sender, e) =>
                        {
                            if (!doneCts.IsCancellationRequested)
                                doneCts.Cancel();
                        };

                        inbox.CountChanged += countChangedHandler;

                        try
                        {
                            // Done message will automatically be sent when this returns
                            await client.IdleAsync(doneCts.Token);
                        }
                        catch (ImapCommandException ex)
                        {
                            ProgramHelpers.Log($"IMAP Command error: {ex.Message}");
                            break; // Break the inner loop to reconnect
                        }
                        catch (ImapProtocolException ex)
                        {
                            ProgramHelpers.Log($"IMAP Protocol error: {ex.Message}");
                            break; // Break the inner loop to reconnect
                        }
                        catch (IOException ex)
                        {
                            ProgramHelpers.Log($"IO error: {ex.Message}");
                            break; // Break the inner loop to reconnect
                        }
                        finally
                        {
                            // Unsubscribe to prevent multiple handlers
                            inbox.CountChanged -= countChangedHandler;
                        }

                        // Check for new messages after exiting IDLE
                        int currentCount = inbox.Count;
                        if (currentCount > initialMessageCount)
                        {
                            int newMessages = currentCount - initialMessageCount;
                            ProgramHelpers.Log($"Received {newMessages} new message(s)!");

                            // Fetch each new message
                            for (int i = initialMessageCount; i < currentCount; i++)
                            {
                                var message = await inbox.GetMessageAsync(i, cancellationToken);
                                await NotifyNewEmailAsync(message, programConfig.sendRawTextAsAttachment, cancellationToken);
                            }

                            initialMessageCount = currentCount;
                        }

                        // Short delay before re-entering IDLE
                        await Task.Delay(1000, cancellationToken);
                    }

                    // If we're here, either the connection was lost or cancellation was requested
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        ProgramHelpers.Log("Connection lost. Will try to reconnect...");

                        // Try to disconnect cleanly if still connected
                        if (client.IsConnected)
                        {
                            try
                            {
                                await client.DisconnectAsync(true, CancellationToken.None);
                            }
                            catch
                            {
                                // Ignore exceptions during disconnect
                            }
                        }

                        // Wait before reconnecting
                        await Task.Delay(5000, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    ProgramHelpers.Log($"Error during monitoring: {ex.Message}");

                    // Wait before reconnecting
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        ProgramHelpers.Log("Will try to reconnect in 30 seconds...");
                        await Task.Delay(30000, cancellationToken);
                    }
                }
            }

            // Disconnect when done if still connected
            if (client.IsConnected)
            {
                ProgramHelpers.Log("Disconnecting from IMAP server...");
                await client.DisconnectAsync(true, CancellationToken.None);
            }
        }

        private static async Task NotifyNewEmailAsync(MimeMessage message, bool sendRawTextAsAttachment, CancellationToken cancellationToken)
        {
            if (messageBuilder is null)
            {
                throw new ArgumentNullException(nameof(messageBuilder));
            }

            if (programConfig is null)
            {
                throw new ArgumentNullException(nameof(programConfig));
            }

            if (TextMatcher.ShouldIgnore(message.Subject, programConfig.ignorePatterns))
            {
                ProgramHelpers.Log($"Notification not sent, ignored ({message.Subject})");
                return;
            }

            var parseMode = ParseMode.MarkdownV2;
            (StringBuilder preview, string rawText) notification = default;
            // Send the notification via Telegram
            var botClient = new TelegramBotClient(programConfig.telegramBotToken!);

            try
            {
                notification = messageBuilder.BuildMessage(message, parseMode);

                if (!sendRawTextAsAttachment || notification.rawText.Length < 1024)
                {
                    await botClient.SendMessage(
                        chatId: programConfig.telegramChatId,
                        text: notification.preview.ToString(),
                        parseMode: parseMode,
                        cancellationToken: cancellationToken
                    );
                }
                else
                {
                    // Convert string to MemoryStream
                    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(notification.rawText));

                    // Send as document
                    await botClient.SendDocument(chatId: programConfig.telegramChatId,
                        document: new InputFileStream(stream, $"message_{message.MessageId}.txt"),
                        parseMode: parseMode,
                        caption: notification.preview.ToString(),
                        disableNotification: true,
                        cancellationToken: cancellationToken
                        );
                }

                 ProgramHelpers.Log($"Notification sent for email from {message.From}");
            }
            catch (Exception ex)
            {
                try
                {
                    ProgramHelpers.Log($"Error sending notification with parse mode {parseMode}, try to send without formatting");
                    ProgramHelpers.Log(notification.ToString());
                    ProgramHelpers.Log($"Errors sending notification: {ex.Message}");
                    await botClient.SendMessage(
                        chatId: programConfig.telegramChatId,
                        text: notification.ToString(),
                        parseMode: ParseMode.None,
                        cancellationToken: cancellationToken
                    );
                }
                catch (Exception ex2)
                {
                    ProgramHelpers.Log($"Error sending notification with parse mode {ParseMode.None}");
                    ProgramHelpers.Log($"Errors sending notification: {ex2.Message}");
                }
            }
        }

        private static IConfigurationRoot LoadConfiguration(string configName)
        {
            string userFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string configFile = $"{configName}.appsettings.json";
            string userConfigFile = $"{userFolder}{Path.DirectorySeparatorChar}{configName}.appsettings.json";
            string systemConfigFile = $"/etc/imap-telegram-notifier/{configName}.appsettings.json";

            if (File.Exists(userConfigFile))
            {
                configFile = userConfigFile;
            }
            else if(File.Exists(systemConfigFile))
            {
                configFile = systemConfigFile;
            }

            var configuration = new ConfigurationBuilder()
                    .AddJsonFile("appsettings.json", optional: true)
                    .AddJsonFile(configFile, optional: false, reloadOnChange: true)
                    .AddEnvironmentVariables()
                    .Build();
            ProgramHelpers.Log($"Config loaded from {configFile}");

            // Register a change callback
            ChangeToken.OnChange(
                configuration.GetReloadToken,
                () => {
                    ProgramHelpers.Log($"Configuration was updated at {DateTime.Now}");
                });
            return configuration;
        }

        private static IProgramConfig BuildConfiguration(IConfigurationRoot config)
        {
            IProgramConfig programConfig = new();
            try
            {
                programConfig.imapServer = config["Imap:Server"];
                
                if (!int.TryParse(config["Imap:Port"], out int port))
                {
                    port = 143;
                    ProgramHelpers.Log($"Warning: Invalid IMAP port specified, using default {port}");
                }

                programConfig.imapPort = port;
                programConfig.emailUsername = config["Imap:Username"];
                programConfig.emailPassword = config["Imap:Password"];
                programConfig.mailboxToMonitor = config["Imap:Mailbox"] ?? "INBOX";
                
                if (!bool.TryParse(config["Imap:UseSsl"], out bool ssl))
                {
                    ssl = true;
                    ProgramHelpers.Log("Warning: Invalid SSL setting specified, using default true");
                }

                programConfig.useSSL = ssl;
                programConfig.telegramBotToken = config["Telegram:BotToken"];
                
                if (!long.TryParse(config["Telegram:ChatId"], out long chatId))
                {
                    chatId = 0;
                    ProgramHelpers.Log("Warning: Invalid Telegram chat ID specified, using default 0");
                }

                programConfig.telegramChatId = chatId;

                var templateConfig = new Template();
                config.GetSection("template").Bind(templateConfig);
                programConfig.template = templateConfig;
                string[] ignorePatterns = config.GetSection("ignorePatterns").Get<string[]>() ?? [];
                programConfig.ignorePatterns = ignorePatterns;

                // Validate required settings
                var missingFields = new List<string>();

                if (string.IsNullOrEmpty(programConfig.imapServer))
                    missingFields.Add("Imap:Server");
                if (string.IsNullOrEmpty(programConfig.emailUsername))
                    missingFields.Add("Imap:Username");
                if (string.IsNullOrEmpty(programConfig.emailPassword))
                    missingFields.Add("Imap:Password");
                if (string.IsNullOrEmpty(programConfig.telegramBotToken))
                    missingFields.Add("Telegram:BotToken");

                if (missingFields.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"Missing required configuration settings: {string.Join(", ", missingFields)}");
                }

                ProgramHelpers.Log("Configuration loaded successfully");
            }
            catch (Exception ex)
            {
                ProgramHelpers.Log($"Error loading configuration: {ex.Message}");
                throw;
            }
            return programConfig;
        }
    }
}