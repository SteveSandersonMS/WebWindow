using System;
using System.IO;
using System.Diagnostics;
using WebWindows;
using System.Threading.Tasks; 
namespace HelloWorldApp
{
    class Program
    {
        static void Main(string[] args)
        {
            string folder = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
            var window = new WebWindow("My first WebWindow app");
            window.SetIconFile(folder + "/icon.png");
	    window.NavigateToLocalFile(folder +"/wwwroot/index.html");
            window.ShowNotification("Oh yeeees!", "This is a notification.");
            window.WaitForExit();
        }
    }
}
