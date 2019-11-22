using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace WebWindows
{
    public class WebWindow : IDisposable
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void OnWebMessageReceivedCallback([MarshalAs(UnmanagedType.LPUTF8Str)] string message);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr OnWebResourceRequestedCallback([MarshalAs(UnmanagedType.LPUTF8Str)] string url, out int numBytes, [MarshalAs(UnmanagedType.LPUTF8Str)] out string contentType);

        const string DllName = "WebWindow.Native";
        [DllImport(DllName)] static extern IntPtr WebWindow_register_win32(IntPtr hInstance);
        [DllImport(DllName)] static extern IntPtr WebWindow_register_mac();
        [DllImport(DllName)] static extern IntPtr WebWindow_ctor([MarshalAs(UnmanagedType.LPUTF8Str)] string title, IntPtr parentWebWindow, IntPtr webMessageReceivedCallback);
        [DllImport(DllName)] static extern IntPtr WebWindow_getHwnd_win32(IntPtr instance);
        [DllImport(DllName)] static extern void WebWindow_SetTitle(IntPtr instance, [MarshalAs(UnmanagedType.LPUTF8Str)] string title);
        [DllImport(DllName)] static extern void WebWindow_Show(IntPtr instance);
        [DllImport(DllName)] static extern void WebWindow_WaitForExit(IntPtr instance);
        [DllImport(DllName)] static extern void WebWindow_Invoke(IntPtr instance, IntPtr callback);
        [DllImport(DllName)] static extern void WebWindow_NavigateToString(IntPtr instance, [MarshalAs(UnmanagedType.LPUTF8Str)] string content);
        [DllImport(DllName)] static extern void WebWindow_NavigateToUrl(IntPtr instance, [MarshalAs(UnmanagedType.LPUTF8Str)] string url);
        [DllImport(DllName)] static extern void WebWindow_ShowMessage(IntPtr instance, [MarshalAs(UnmanagedType.LPUTF8Str)] string title, [MarshalAs(UnmanagedType.LPUTF8Str)] string body, uint type);
        [DllImport(DllName)] static extern void WebWindow_SendMessage(IntPtr instance, [MarshalAs(UnmanagedType.LPUTF8Str)] string message);
        [DllImport(DllName)] static extern void WebWindow_AddCustomScheme(IntPtr instance, [MarshalAs(UnmanagedType.LPUTF8Str)] string scheme, IntPtr requestHandler);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] static extern IntPtr SendMessage(IntPtr hWnd, UInt32 Msg, IntPtr wParam, IntPtr lParam);

        private List<GCHandle> _gcHandlesToFree = new List<GCHandle>();
        private IntPtr _nativeWebWindow;
        private string _title;

        static WebWindow()
        {
            // Workaround for a crashing issue on Linux. Without this, applications
            // are crashing when running in Debug mode (but not Release) if the very
            // first line of code in Program::Main references the WebWindow type.
            // It's unclear why.
            Thread.Sleep(1);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var hInstance = Marshal.GetHINSTANCE(typeof(WebWindow).Module);
                WebWindow_register_win32(hInstance);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                WebWindow_register_mac();
            }
        }

        public WebWindow(string title) : this(title, _ => { })
        {
        }

        public WebWindow(string title, Action<WebWindowOptions> configure)
        {
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            var options = new WebWindowOptions();
            configure.Invoke(options);

            WriteTitleField(title);

            var onWebMessageReceivedDelegate = (OnWebMessageReceivedCallback)ReceiveWebMessage;
            _gcHandlesToFree.Add(GCHandle.Alloc(onWebMessageReceivedDelegate));
            var onWebMessageReceivedPtr = Marshal.GetFunctionPointerForDelegate(onWebMessageReceivedDelegate);

            var parentPtr = options.Parent?._nativeWebWindow ?? default;
            _nativeWebWindow = WebWindow_ctor(_title, parentPtr, onWebMessageReceivedPtr);

            foreach (var (schemeName, handler) in options.SchemeHandlers)
            {
                AddCustomScheme(schemeName, handler);
            }

            // Auto-show to simplify the API, but more importantly because you can't
            // do things like navigate until it has been shown
            Show();
        }

        ~WebWindow()
        {
            Dispose(false);
        }

        public void Show() => WebWindow_Show(_nativeWebWindow);
        public void WaitForExit() => WebWindow_WaitForExit(_nativeWebWindow);

        public string Title
        {
            get => _title;
            set
            {
                WriteTitleField(value);
                WebWindow_SetTitle(_nativeWebWindow, _title);
            }
        }

        public void ShowMessage(string title, string body)
        {
            WebWindow_ShowMessage(_nativeWebWindow, title, body, /* MB_OK */ 0);
        }

        public void Invoke(Action workItem)
        {
            var fnPtr = Marshal.GetFunctionPointerForDelegate(workItem);
            WebWindow_Invoke(_nativeWebWindow, fnPtr);
            GC.KeepAlive(fnPtr);
        }

        public IntPtr Hwnd
        {
            get
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return WebWindow_getHwnd_win32(_nativeWebWindow);
                }
                else
                {
                    throw new PlatformNotSupportedException($"{nameof(Hwnd)} is only supported on Windows");
                }
            }
        }

        public void NavigateToString(string content)
        {
            WebWindow_NavigateToString(_nativeWebWindow, content);
        }

        public void NavigateToUrl(string url)
        {
            WebWindow_NavigateToUrl(_nativeWebWindow, url);
        }

        public void NavigateToLocalFile(string path)
        {
            var absolutePath = Path.GetFullPath(path);
            var url = new Uri(absolutePath, UriKind.Absolute);
            NavigateToUrl(url.ToString());
        }

        public void SendMessage(string message)
        {
            WebWindow_SendMessage(_nativeWebWindow, message);
        }

        public event EventHandler<string> OnWebMessageReceived;

        private void WriteTitleField(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                value = "Untitled window";
            }

            // Due to Linux/Gtk platform limitations, the window title has to be no more than 31 chars
            if (value.Length > 31 && RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                value = value.Substring(0, 31);
            }

            _title = value;
        }

        private void ReceiveWebMessage([MarshalAs(UnmanagedType.LPUTF8Str)] string message)
        {
            OnWebMessageReceived?.Invoke(this, message);
        }

        private void AddCustomScheme(string scheme, ResolveWebResourceDelegate requestHandler)
        {
            // Because of WKWebView limitations, this can only be called during the constructor
            // before the first call to Show. To enforce this, it's private and is only called
            // in response to the constructor options.
            OnWebResourceRequestedCallback callback = (string url, out int numBytes, out string contentType) =>
            {
                var responseStream = requestHandler(url, out contentType);
                if (responseStream == null)
                {
                    // Webview should pass through request to normal handlers (e.g., network)
                    // or handle as 404 otherwise
                    numBytes = 0;
                    return default;
                }

                // Read the stream into memory and serve the bytes
                // In the future, it would be possible to pass the stream through into C++
                using (responseStream)
                using (var ms = new MemoryStream())
                {
                    responseStream.CopyTo(ms);

                    numBytes = (int)ms.Position;
                    var buffer = Marshal.AllocHGlobal(numBytes);
                    Marshal.Copy(ms.GetBuffer(), 0, buffer, numBytes);
                    return buffer;
                }
            };

            _gcHandlesToFree.Add(GCHandle.Alloc(callback));

            var callbackPtr = Marshal.GetFunctionPointerForDelegate(callback);
            WebWindow_AddCustomScheme(_nativeWebWindow, scheme, callbackPtr);
        }

        public void Close()
        {
            SendMessage(Hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero);
            Dispose();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(Boolean itIsSafeToAlsoFreeManagedObjects)
        {
            foreach (var gcHandle in _gcHandlesToFree)
            {
                gcHandle.Free();
            }
            _nativeWebWindow = IntPtr.Zero;

            if (itIsSafeToAlsoFreeManagedObjects)
            {
                _gcHandlesToFree.Clear();
            }
        }
    }
}
