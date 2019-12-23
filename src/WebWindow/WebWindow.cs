using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace WebWindows
{
    [StructLayout(LayoutKind.Sequential)]
    struct NativeRect
    {
        public int x, y;
        public int width, height;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct NativeMonitor
    {
        public NativeRect monitor;
        public NativeRect work;
    }

    public readonly struct Monitor
    {
        public readonly Rectangle MonitorArea;
        public readonly Rectangle WorkArea;

        public Monitor(Rectangle monitor, Rectangle work)
        {
            MonitorArea = monitor;
            WorkArea = work;
        }

        internal Monitor(NativeRect monitor, NativeRect work)
            : this(new Rectangle(monitor.x, monitor.y, monitor.width, monitor.height), new Rectangle(work.x, work.y, work.width, work.height))
        { }

        internal Monitor(NativeMonitor nativeMonitor)
            : this(nativeMonitor.monitor, nativeMonitor.work)
        { }
    }

    public class WebWindow
    {
        // Here we use auto charset instead of forcing UTF-8.
        // Thus the native code for Windows will be much more simple.
        // Auto charset is UTF-16 on Windows and UTF-8 on Unix(.NET Core 3.0 and later and Mono).
        // As we target .NET Standard 2.1, we assume it runs on .NET Core 3.0 and later.
        // We should specify using auto charset because the default value is ANSI.

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Auto)] delegate void OnWebMessageReceivedCallback(string message);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Auto)] delegate IntPtr OnWebResourceRequestedCallback(string url, out int numBytes, out string contentType);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void InvokeCallback();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GetAllMonitorsCallback(in NativeMonitor monitor);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void ResizedCallback(int width, int height);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void MovedCallback(int x, int y);

        const string DllName = "WebWindow.Native";
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr WebWindow_register_win32(IntPtr hInstance);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr WebWindow_register_mac();
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Auto)] static extern IntPtr WebWindow_ctor(string title, IntPtr parentWebWindow, OnWebMessageReceivedCallback webMessageReceivedCallback);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] static extern void WebWindow_dtor(IntPtr instance);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr WebWindow_getHwnd_win32(IntPtr instance);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Auto)] static extern void WebWindow_SetTitle(IntPtr instance, string title);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] static extern void WebWindow_Show(IntPtr instance);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] static extern void WebWindow_WaitForExit(IntPtr instance);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] static extern void WebWindow_Invoke(IntPtr instance, InvokeCallback callback);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Auto)] static extern void WebWindow_NavigateToString(IntPtr instance, string content);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Auto)] static extern void WebWindow_NavigateToUrl(IntPtr instance, string url);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Auto)] static extern void WebWindow_ShowMessage(IntPtr instance, string title, string body, uint type);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Auto)] static extern void WebWindow_SendMessage(IntPtr instance, string message);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Auto)] static extern void WebWindow_AddCustomScheme(IntPtr instance, string scheme, OnWebResourceRequestedCallback requestHandler);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] static extern void WebWindow_SetResizable(IntPtr instance, int resizable);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] static extern void WebWindow_GetSize(IntPtr instance, out int width, out int height);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] static extern void WebWindow_SetSize(IntPtr instance, int width, int height);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] static extern void WebWindow_SetResizedCallback(IntPtr instance, ResizedCallback callback);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] static extern void WebWindow_GetAllMonitors(IntPtr instance, GetAllMonitorsCallback callback);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] static extern uint WebWindow_GetScreenDpi(IntPtr instance);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] static extern void WebWindow_GetPosition(IntPtr instance, out int x, out int y);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] static extern void WebWindow_SetPosition(IntPtr instance, int x, int y);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] static extern void WebWindow_SetMovedCallback(IntPtr instance, MovedCallback callback);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] static extern void WebWindow_SetTopmost(IntPtr instance, int topmost);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Auto)] static extern void WebWindow_SetIconFile(IntPtr instance, string filename);

        private readonly List<GCHandle> _gcHandlesToFree = new List<GCHandle>();
        private readonly List<IntPtr> _hGlobalToFree = new List<IntPtr>();
        private readonly IntPtr _nativeWebWindow;
        private readonly int _ownerThreadId;
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
            _ownerThreadId = Thread.CurrentThread.ManagedThreadId;

            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            var options = new WebWindowOptions();
            configure.Invoke(options);

            WriteTitleField(title);

            var onWebMessageReceivedDelegate = (OnWebMessageReceivedCallback)ReceiveWebMessage;
            _gcHandlesToFree.Add(GCHandle.Alloc(onWebMessageReceivedDelegate));

            var parentPtr = options.Parent?._nativeWebWindow ?? default;
            _nativeWebWindow = WebWindow_ctor(_title, parentPtr, onWebMessageReceivedDelegate);

            foreach (var (schemeName, handler) in options.SchemeHandlers)
            {
                AddCustomScheme(schemeName, handler);
            }

            var onResizedDelegate = (ResizedCallback)OnResized;
            _gcHandlesToFree.Add(GCHandle.Alloc(onResizedDelegate));
            WebWindow_SetResizedCallback(_nativeWebWindow, onResizedDelegate);

            var onMovedDelegate = (MovedCallback)OnMoved;
            _gcHandlesToFree.Add(GCHandle.Alloc(onMovedDelegate));
            WebWindow_SetMovedCallback(_nativeWebWindow, onMovedDelegate);

            // Auto-show to simplify the API, but more importantly because you can't
            // do things like navigate until it has been shown
            Show();
        }

        ~WebWindow()
        {
            // TODO: IDisposable
            WebWindow_SetResizedCallback(_nativeWebWindow, null);
            WebWindow_SetMovedCallback(_nativeWebWindow, null);
            foreach (var gcHandle in _gcHandlesToFree)
            {
                gcHandle.Free();
            }
            _gcHandlesToFree.Clear();
            foreach (var handle in _hGlobalToFree)
            {
                Marshal.FreeHGlobal(handle);
            }
            _hGlobalToFree.Clear();
            WebWindow_dtor(_nativeWebWindow);
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
            // If we're already on the UI thread, no need to dispatch
            if (Thread.CurrentThread.ManagedThreadId == _ownerThreadId)
            {
                workItem();
            }
            else
            {
                WebWindow_Invoke(_nativeWebWindow, workItem.Invoke);
            }
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

        private void ReceiveWebMessage(string message)
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
                    _hGlobalToFree.Add(buffer);
                    return buffer;
                }
            };

            _gcHandlesToFree.Add(GCHandle.Alloc(callback));
            WebWindow_AddCustomScheme(_nativeWebWindow, scheme, callback);
        }

        private bool _resizable = true;
        public bool Resizable
        {
            get => _resizable;
            set
            {
                if (_resizable != value)
                {
                    _resizable = value;
                    Invoke(() => WebWindow_SetResizable(_nativeWebWindow, _resizable ? 1 : 0));
                }
            }
        }

        private int _width;
        private int _height;

        private void GetSize() => WebWindow_GetSize(_nativeWebWindow, out _width, out _height);

        private void SetSize() => Invoke(() => WebWindow_SetSize(_nativeWebWindow, _width, _height));

        public int Width
        {
            get
            {
                GetSize();
                return _width;
            }
            set
            {
                GetSize();
                if (_width != value)
                {
                    _width = value;
                    SetSize();
                }
            }
        }

        public int Height
        {
            get
            {
                GetSize();
                return _height;
            }
            set
            {
                GetSize();
                if (_height != value)
                {
                    _height = value;
                    SetSize();
                }
            }
        }

        public Size Size
        {
            get
            {
                GetSize();
                return new Size(_width, _height);
            }
            set
            {
                if (_width != value.Width || _height != value.Height)
                {
                    _width = value.Width;
                    _height = value.Height;
                    SetSize();
                }
            }
        }

        private void OnResized(int width, int height) => SizeChanged?.Invoke(this, new Size(width, height));

        public event EventHandler<Size> SizeChanged;

        private int _x;
        private int _y;

        private void GetPosition() => WebWindow_GetPosition(_nativeWebWindow, out _x, out _y);

        private void SetPosition() => Invoke(() => WebWindow_SetPosition(_nativeWebWindow, _x, _y));

        public int Left
        {
            get
            {
                GetPosition();
                return _x;
            }
            set
            {
                GetPosition();
                if (_x != value)
                {
                    _x = value;
                    SetPosition();
                }
            }
        }

        public int Top
        {
            get
            {
                GetPosition();
                return _y;
            }
            set
            {
                GetPosition();
                if (_y != value)
                {
                    _y = value;
                    SetPosition();
                }
            }
        }

        public Point Location
        {
            get
            {
                GetPosition();
                return new Point(_x, _y);
            }
            set
            {
                if (_x != value.X || _y != value.Y)
                {
                    _x = value.X;
                    _y = value.Y;
                    SetPosition();
                }
            }
        }

        private void OnMoved(int x, int y) => LocationChanged?.Invoke(this, new Point(x, y));

        public event EventHandler<Point> LocationChanged;

        public IReadOnlyList<Monitor> Monitors
        {
            get
            {
                List<Monitor> monitors = new List<Monitor>();
                int callback(in NativeMonitor monitor)
                {
                    monitors.Add(new Monitor(monitor));
                    return 1;
                }
                WebWindow_GetAllMonitors(_nativeWebWindow, callback);
                return monitors;
            }
        }

        public uint ScreenDpi => WebWindow_GetScreenDpi(_nativeWebWindow);

        private bool _topmost = false;
        public bool Topmost
        {
            get => _topmost;
            set
            {
                if (_topmost != value)
                {
                    _topmost = value;
                    Invoke(() => WebWindow_SetTopmost(_nativeWebWindow, _topmost ? 1 : 0));
                }
            }
        }

        public void SetIconFile(string filename) => WebWindow_SetIconFile(_nativeWebWindow, Path.GetFullPath(filename));
    }
}
