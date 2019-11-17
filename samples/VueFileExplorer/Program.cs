using System;
using System.IO;
using System.Linq;
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
                    var destinationPath = Path.GetFullPath(Path.Combine(basePath, relativePath));
                    ShowDirectoryInfo(window, destinationPath);
                    break;
            }
        }

        static void ShowDirectoryInfo(WebWindow window, string path)
        {
            var directoryInfo = new DirectoryInfo(path);
            SendCommand(window, "show", new
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
                }),
            });
        }

        static void SendCommand(WebWindow window, string commandName, object arg)
        {
            window.SendMessage(JsonSerializer.Serialize(new { command = commandName, arg = arg }));
        }
    }
}
