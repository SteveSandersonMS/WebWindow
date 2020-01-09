using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WebWindows.Blazor
{
    public static class ComponentsDesktop
    {
        internal static string InitialUriAbsolute { get; private set; }
        internal static string BaseUriAbsolute { get; private set; }
        internal static DesktopJSRuntime DesktopJSRuntime { get; private set; }
        internal static DesktopRenderer DesktopRenderer { get; private set; }
        internal static WebWindow WebWindow { get; private set; }

        public static void Run<TStartup>(string windowTitle, string hostHtmlPath)
        {
            DesktopSynchronizationContext.UnhandledException += (sender, exception) =>
            {
                UnhandledException(exception);
            };

            WebWindow = new WebWindow(windowTitle, options =>
            {
                var contentRootAbsolute = Path.GetDirectoryName(Path.GetFullPath(hostHtmlPath));

                options.SchemeHandlers.Add(BlazorAppScheme, (string url, out string contentType) =>
                {
                    // TODO: Only intercept for the hostname 'app' and passthrough for others
                    // TODO: Prevent directory traversal?
                    var appFile = Path.Combine(contentRootAbsolute, new Uri(url).AbsolutePath.Substring(1));
                    if (appFile == contentRootAbsolute)
                    {
                        appFile = hostHtmlPath;
                    }

                    contentType = GetContentType(appFile);
                    return File.Exists(appFile) ? File.OpenRead(appFile) : null;
                });

                // framework:// is resolved as embedded resources
                options.SchemeHandlers.Add("framework", (string url, out string contentType) =>
                {
                    contentType = GetContentType(url);
                    return SupplyFrameworkFile(url);
                });
            });

            CancellationTokenSource appLifetimeCts = new CancellationTokenSource();
            Task.Factory.StartNew(async () =>
            {
                try
                {
                    var ipc = new IPC(WebWindow);
                    await RunAsync<TStartup>(ipc, appLifetimeCts.Token);
                }
                catch (Exception ex)
                {
                    UnhandledException(ex);
                    throw;
                }
            });

            try
            {
                WebWindow.NavigateToUrl(BlazorAppScheme + "://app/");
                WebWindow.WaitForExit();
            }
            finally
            {
                appLifetimeCts.Cancel();
            }
        }

        private static string GetContentType(string url)
        {
            var ext = Path.GetExtension(url);
            switch (ext)
            {
                case ".html": return "text/html";
                case ".css": return "text/css";
                case ".js": return "text/javascript";
                case ".wasm": return "application/wasm";
            }
            return "application/octet-stream";
        }

        private static string BlazorAppScheme
        {
            get
            {
                // On Windows, we can't use a custom scheme to host the initial HTML,
                // because webview2 won't let you do top-level navigation to such a URL.
                // On Linux/Mac, we must use a custom scheme, because their webviews
                // don't have a way to intercept http:// scheme requests.
                return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? "http"
                    : "app";
            }
        }

        private static void UnhandledException(Exception ex)
        {
            WebWindow.ShowMessage("Error", $"{ex.Message}\n{ex.StackTrace}");
        }

        private static async Task RunAsync<TStartup>(IPC ipc, CancellationToken appLifetime)
        {
            var configurationBuilder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true);

            DesktopJSRuntime = new DesktopJSRuntime(ipc);
            await PerformHandshakeAsync(ipc);
            AttachJsInterop(ipc, appLifetime);

            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSingleton<IConfiguration>(configurationBuilder.Build());
            serviceCollection.AddLogging(configure => configure.AddConsole());
            serviceCollection.AddSingleton<NavigationManager>(DesktopNavigationManager.Instance);
            serviceCollection.AddSingleton<IJSRuntime>(DesktopJSRuntime);
            serviceCollection.AddSingleton<INavigationInterception, DesktopNavigationInterception>();
            serviceCollection.AddSingleton(WebWindow);

            var startup = new ConventionBasedStartup(Activator.CreateInstance(typeof(TStartup)));
            startup.ConfigureServices(serviceCollection);

            var services = serviceCollection.BuildServiceProvider();
            var builder = new DesktopApplicationBuilder(services);
            startup.Configure(builder, services);

            var loggerFactory = services.GetRequiredService<ILoggerFactory>();

            DesktopRenderer = new DesktopRenderer(services, ipc, loggerFactory);
            DesktopRenderer.UnhandledException += (sender, exception) =>
            {
                Console.Error.WriteLine(exception);
            };

            foreach (var rootComponent in builder.Entries)
            {
                _ = DesktopRenderer.AddComponentAsync(rootComponent.componentType, rootComponent.domElementSelector);
            }
        }

        private static Stream SupplyFrameworkFile(string uri)
        {
            switch (uri)
            {
                case "framework://blazor.desktop.js":
                    return typeof(ComponentsDesktop).Assembly.GetManifestResourceStream("WebWindow.Blazor.blazor.desktop.js");
                default:
                    throw new ArgumentException($"Unknown framework file: {uri}");
            }
        }

        private static async Task PerformHandshakeAsync(IPC ipc)
        {
            var tcs = new TaskCompletionSource<object>();
            ipc.Once("components:init", args =>
            {
                var argsArray = (object[])args;
                InitialUriAbsolute = ((JsonElement)argsArray[0]).GetString();
                BaseUriAbsolute = ((JsonElement)argsArray[1]).GetString();

                tcs.SetResult(null);
            });

            await tcs.Task;
        }

        private static void AttachJsInterop(IPC ipc, CancellationToken appLifetime)
        {
            var desktopSynchronizationContext = new DesktopSynchronizationContext(appLifetime);
            SynchronizationContext.SetSynchronizationContext(desktopSynchronizationContext);

            ipc.On("BeginInvokeDotNetFromJS", args =>
            {
                desktopSynchronizationContext.Send(state =>
                {
                    var argsArray = (object[])state;
                    DotNetDispatcher.BeginInvokeDotNet(
                        DesktopJSRuntime,
                        new DotNetInvocationInfo(
                            assemblyName: ((JsonElement)argsArray[1]).GetString(),
                            methodIdentifier: ((JsonElement)argsArray[2]).GetString(),
                            dotNetObjectId: ((JsonElement)argsArray[3]).GetInt64(),
                            callId: ((JsonElement)argsArray[0]).GetString()),
                        ((JsonElement)argsArray[4]).GetString());
                }, args);
            });

            ipc.On("EndInvokeJSFromDotNet", args =>
            {
                desktopSynchronizationContext.Send(state =>
                {
                    var argsArray = (object[])state;
                    DotNetDispatcher.EndInvokeJS(
                        DesktopJSRuntime,
                        ((JsonElement)argsArray[2]).GetString());
                }, args);
            });
        }

        private static void Log(string message)
        {
            var process = Process.GetCurrentProcess();
            Console.WriteLine($"[{process.ProcessName}:{process.Id}] out: " + message);
        }
    }
}
