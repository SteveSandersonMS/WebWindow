using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace WebWindows.Blazor
{
    // Many aspects of the layering here are not what we really want, but it won't affect
    // people prototyping applications with it. We can put more work into restructuring the
    // hosting and startup models in the future if it's justified.

    internal class DesktopRenderer : Renderer
    {
        private const int RendererId = 0; // Not relevant, since we have only one renderer in Desktop
        private readonly IPC _ipc;
        private readonly IJSRuntime _jsRuntime;
        private static readonly Type _writer;
        private static readonly MethodInfo _writeMethod;

        public override Dispatcher Dispatcher { get; } = NullDispatcher.Instance;

        static DesktopRenderer()
        {
            _writer = typeof(RenderBatchWriter);
            _writeMethod = _writer.GetMethod("Write", new[] { typeof(RenderBatch).MakeByRefType() });
        }

        public DesktopRenderer(IServiceProvider serviceProvider, IPC ipc, ILoggerFactory loggerFactory)
            : base(serviceProvider, loggerFactory)
        {
            _ipc = ipc ?? throw new ArgumentNullException(nameof(ipc));
            _jsRuntime = serviceProvider.GetRequiredService<IJSRuntime>();
        }

        /// <summary>
        /// Notifies when a rendering exception occured.
        /// </summary>
        public event EventHandler<Exception> UnhandledException;

        /// <summary>
        /// Attaches a new root component to the renderer,
        /// causing it to be displayed in the specified DOM element.
        /// </summary>
        /// <typeparam name="TComponent">The type of the component.</typeparam>
        /// <param name="domElementSelector">A CSS selector that uniquely identifies a DOM element.</param>
        public Task AddComponentAsync<TComponent>(string domElementSelector)
            where TComponent : IComponent
        {
            return AddComponentAsync(typeof(TComponent), domElementSelector);
        }

        /// <summary>
        /// Associates the <see cref="IComponent"/> with the <see cref="BrowserRenderer"/>,
        /// causing it to be displayed in the specified DOM element.
        /// </summary>
        /// <param name="componentType">The type of the component.</param>
        /// <param name="domElementSelector">A CSS selector that uniquely identifies a DOM element.</param>
        public Task AddComponentAsync(Type componentType, string domElementSelector)
        {
            var component = InstantiateComponent(componentType);
            var componentId = AssignRootComponentId(component);

            var attachComponentTask = _jsRuntime.InvokeAsync<object>(
                "Blazor._internal.attachRootComponentToElement",
                domElementSelector,
                componentId,
                RendererId);
            CaptureAsyncExceptions(attachComponentTask);
            return RenderRootComponentAsync(componentId);
        }

        /// <inheritdoc />
        protected override Task UpdateDisplayAsync(in RenderBatch batch)
        {
            string base64;
            using (var memoryStream = new MemoryStream())
            {
                object renderBatchWriter = Activator.CreateInstance(_writer, new object[] { memoryStream, false });
                using (renderBatchWriter as IDisposable)
                {
                    _writeMethod.Invoke(renderBatchWriter, new object[] { batch });
                }

                var batchBytes = memoryStream.ToArray();
                base64 = Convert.ToBase64String(batchBytes);
            }

            _ipc.Send("JS.RenderBatch", RendererId, base64);

            // TODO: Consider finding a way to get back a completion message from the Desktop side
            // in case there was an error. We don't really need to wait for anything to happen, since
            // this is not prerendering and we don't care how quickly the UI is updated, but it would
            // be desirable to flow back errors.
            return Task.CompletedTask;
        }

        private async void CaptureAsyncExceptions(ValueTask<object> task)
        {
            try
            {
                await task;
            }
            catch (Exception ex)
            {
                UnhandledException?.Invoke(this, ex);
            }
        }

        protected override void HandleException(Exception exception)
        {
            Console.WriteLine(exception.ToString());
        }
    }
}
