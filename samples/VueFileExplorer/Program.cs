using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using WebWindows;

namespace VueFileExplorer
{
    class Program
    {
        static void Main(string[] args)
        {
            var window = new WebWindow(".NET Core + Vue.js file explorer");
            window.OnWebMessageReceived += HandleWebMessageReceived;
            window.NavigateToLocalFile("wwwroot/index.html");
            window.WaitForExit();
        }

        static void HandleWebMessageReceived(object sender, string message)
        {
            var window = (WebWindow)sender;
            var parsedMessage = JsonDocument.Parse(message).RootElement;
            switch (parsedMessage.GetProperty("command").GetString())
            {
                case "ready":
                    ShowDirectoryInfo(window, Directory.GetCurrentDirectory());
                    break;
                case "navigateTo":
                    var basePath = parsedMessage.GetProperty("basePath").GetString();
                    var relativePath = parsedMessage.GetProperty("relativePath").GetString();
                    var destinationPath = Path.GetFullPath(Path.Combine(basePath, relativePath)).TrimEnd(Path.DirectorySeparatorChar);
                    ShowDirectoryInfo(window, destinationPath);
                    break;
                case "showFile":
                    var fullName = parsedMessage.GetProperty("fullName").GetString();
                    ShowFileContents(window, fullName);
                    break;
            }
        }

        static void ShowDirectoryInfo(WebWindow window, string path)
        {
            window.Title = Path.GetFileName(path);

            var directoryInfo = new DirectoryInfo(path);
            SendCommand(window, "showDirectory", new
            {
                name = path,
                isRoot = Path.GetDirectoryName(path) == null,
                directories = directoryInfo.GetDirectories().Select(directoryInfo => new
                {
                    name = directoryInfo.Name + Path.DirectorySeparatorChar,
                }),
                files = directoryInfo.GetFiles().Select(fileInfo => new
                {
                    name = fileInfo.Name,
                    size = fileInfo.Length,
                    fullName = fileInfo.FullName,
                }),
            });
        }

        private static void ShowFileContents(WebWindow window, string fullName)
        {
            var fileInfo = new FileInfo(fullName);
            SendCommand(window, "showFile", null); // Clear the old display first
            SendCommand(window, "showFile", new
            {
                name = fileInfo.Name,
                size = fileInfo.Length,
                fullName = fileInfo.FullName,
                text = ReadTextFile(fullName, maxChars: 100000),
            });
        }

        private static string ReadTextFile(string fullName, int maxChars)
        {
            var stringBuilder = new StringBuilder();
            var buffer = new char[4096];
            using (var file = File.OpenText(fullName))
            {
                int charsRead = int.MaxValue;
                while (maxChars > 0 && charsRead > 0)
                {
                    charsRead = file.ReadBlock(buffer, 0, Math.Min(maxChars, buffer.Length));
                    stringBuilder.Append(buffer, 0, charsRead);
                    maxChars -= charsRead;
                }

                return stringBuilder.ToString();
            }
        }

        static void SendCommand(WebWindow window, string commandName, object arg)
        {
            window.SendMessage(JsonSerializer.Serialize(new { command = commandName, arg = arg }));
        }
    }
}
