using System;
using System.Collections.Generic;

namespace WebWindows.Blazor
{
    internal class ComponentsApplicationBuilder : IComponentsApplicationBuilder
    {
        public ComponentsApplicationBuilder(IServiceProvider services)
        {
            Services = services;
            Entries = new List<(Type component_type, string dom_element_selector)>();
        }

        public List<(Type component_type, string dom_element_selector)> Entries { get; }

        public IServiceProvider Services { get; }

        public void AddComponent(Type componentType, string domElementSelector)
        {
            if (componentType == null)
                throw new ArgumentNullException(nameof(componentType));

            if (domElementSelector == null)
                throw new ArgumentNullException(nameof(domElementSelector));

            Entries.Add((componentType, domElementSelector));
        }

        public void AddComponent<T>(string dom_element_selector)
        {
            var component_type = typeof(T);

            AddComponent(component_type, dom_element_selector);
        }
    }
}
