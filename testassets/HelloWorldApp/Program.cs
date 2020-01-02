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
            window.OnUriChange += (sender, uri) =>
            {
                Console.WriteLine($"New URI: {uri}");
            };
            //window.NavigateToLocalFile("wwwroot/index.html");
            window.NavigateToUrl("https://oauth.dev.valididcloud.com/connect/authorize?response_type=code&nonce=zZYFw7ZYTj5xZkqON95gnA&state=-nQotCEKDeCyw5OCFBdDUw&code_challenge=xarZMRurKMtd4vW3a0meG4PKmlGfzBO-UwmTIw_uYfw&code_challenge_method=S256&client_id=00175971-74a5-42af-9946-a21a6c09a93f&scope=https%3A%2F%2Fcap.dev.valididcloud.com%2F&redirect_uri=http%3A%2F%2Flocalhost%3A8080%2Fuser%2Fmobile");
            window.WaitForExit();
        }
    }
}
