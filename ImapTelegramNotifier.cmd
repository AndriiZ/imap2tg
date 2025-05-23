cd ImapTelegramNotifier
dotnet publish -c Release -r linux-arm --self-contained true
cd ..
cd ImapTelegramNotifier.Tests
dotnet test
pause