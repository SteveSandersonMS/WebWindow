using Microsoft.AspNetCore.Components;
using System;
using System.Collections.Generic;

namespace WebWindows.Blazor
{
    public class DesktopApplicationBuilder
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

        public void AddComponent<T>(string domElementSelector) where T : IComponent
            => AddComponent(typeof(T), domElementSelector);
    }
}
