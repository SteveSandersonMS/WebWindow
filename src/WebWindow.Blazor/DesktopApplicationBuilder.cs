using Microsoft.AspNetCore.Components.Builder;
using System;
using System.Collections.Generic;

namespace WebWindows.Blazor
{
    internal class DesktopApplicationBuilder : IComponentsApplicationBuilder
    {
        public DesktopApplicationBuilder(IServiceProvider services)
        {
            Services = services;
            Entries = new List<(Type componentType, string domElementSelector)>();
        }

        public List<(Type componentType, string domElementSelector)> Entries { get; }

        public IServiceProvider Services { get; }

        public void AddComponent(Type componentType, string domElementSelector)
        {
            if (componentType == null)
            {
                throw new ArgumentNullException(nameof(componentType));
            }

            if (domElementSelector == null)
            {
                throw new ArgumentNullException(nameof(domElementSelector));
            }

            Entries.Add((componentType, domElementSelector));
        }
    }
}
