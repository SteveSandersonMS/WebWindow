using System;
using System.IO;
using System.Text;
using WebWindows;

namespace HelloWorldApp
{
    class Program
    {
        static void Main(string[] args)
        {
            var window = new WebWindow("My great app", options =>
            {
                options.SchemeHandlers.Add("app", (string url, out string contentType) =>
                {
                    contentType = "text/javascript";
                    return new MemoryStream(Encoding.UTF8.GetBytes("alert('super')"));
                });
            });

            window.OnWebMessageReceived += (sender, message) =>
            {
                window.SendMessage("Got message: " + message);
            };

            window.NavigateToLocalFile("wwwroot/index.html");
            window.WaitForExit();
        }
    }
}
