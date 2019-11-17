using Microsoft.AspNetCore.Components.Routing;
using System.Threading.Tasks;

namespace WebWindows.Blazor
{
    internal class DesktopNavigationInterception : INavigationInterception
    {
        public Task EnableNavigationInterceptionAsync()
        {
            // We don't actually need to set anything up in this environment
            return Task.CompletedTask;
        }
    }
}