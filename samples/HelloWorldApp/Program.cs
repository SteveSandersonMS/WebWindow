using System;
using WebWindows;

namespace HelloWorldApp
{
    class Program
    {
        static void Main(string[] args)
        {
            var window = new WebWindow("My first WebWindow app");
            window.NavigateToLocalFile("wwwroot/index.html");
            window.WaitForExit();
        }
    }
}
