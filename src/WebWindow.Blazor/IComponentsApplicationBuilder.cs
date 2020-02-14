using System;
using System.Collections.Generic;

namespace WebWindows.Blazor
{
    public interface IComponentsApplicationBuilder
    {
        List<(Type component_type, string dom_element_selector)> Entries { get; }

        IServiceProvider Services { get; }

        void AddComponent(Type componentType, string dom_element_selector);

        void AddComponent<T>(string dom_element_selector);
    }
}