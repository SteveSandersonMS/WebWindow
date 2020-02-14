#ifndef WEBWINDOW_H
#define WEBWINDOW_H

#ifdef _WIN32
#include <Windows.h>
#include <wrl/event.h>
#include <map>
#include <string>
#include <wil/com.h>
#include <WebView2.h>
typedef const wchar_t* AutoString;
#else
#ifdef OS_LINUX
#include <gtk/gtk.h>
#endif
typedef char* AutoString;
#endif

struct Monitor
{
	struct MonitorRect
	{
		int x, y;
		int width, height;
	} monitor, work;
};

typedef void (*ACTION)();
typedef void (*WebMessageReceivedCallback)(AutoString message);
typedef void* (*WebResourceRequestedCallback)(AutoString url, int* outNumBytes, AutoString* outContentType);
typedef int (*GetAllMonitorsCallback)(const Monitor* monitor);
typedef void (*ResizedCallback)(int width, int height);
typedef void (*MovedCallback)(int x, int y);

class WebWindow
{
private:
	WebMessageReceivedCallback _webMessageReceivedCallback;
	MovedCallback _movedCallback;
	ResizedCallback _resizedCallback;
#ifdef _WIN32
	static HINSTANCE _hInstance;
	HWND _hWnd;
	WebWindow* _parent;
	wil::com_ptr<IWebView2Environment3> _webviewEnvironment;
	wil::com_ptr<IWebView2WebView5> _webviewWindow;
	std::map<std::wstring, WebResourceRequestedCallback> _schemeToRequestHandler;
	void AttachWebView();
#elif OS_LINUX
	GtkWidget* _window;
	GtkWidget* _webview;
#elif OS_MAC
	void* _window;
	void* _webview;
	void* _webviewConfiguration;
	void AttachWebView();
#endif

public:
#ifdef _WIN32
	static void Register(HINSTANCE hInstance);
	HWND getHwnd();
	void RefitContent();
#elif OS_MAC
	static void Register();
#endif

	WebWindow(AutoString title, WebWindow* parent, WebMessageReceivedCallback webMessageReceivedCallback);
	~WebWindow();
	void SetTitle(AutoString title);
	void Show();
	void WaitForExit();
	void ShowMessage(AutoString title, AutoString body, unsigned int type);
	void Invoke(ACTION callback);
	void NavigateToUrl(AutoString url);
	void NavigateToString(AutoString content);
	void SendMessage(AutoString message);
	void AddCustomScheme(AutoString scheme, WebResourceRequestedCallback requestHandler);
	void SetResizable(bool resizable);
	void GetSize(int* width, int* height);
	void SetSize(int width, int height);
	void SetResizedCallback(ResizedCallback callback) { _resizedCallback = callback; }
	void InvokeResized(int width, int height) { if (_resizedCallback) _resizedCallback(width, height); }
	void GetAllMonitors(GetAllMonitorsCallback callback);
	unsigned int GetScreenDpi();
	void GetPosition(int* x, int* y);
	void SetPosition(int x, int y);
	void SetMovedCallback(MovedCallback callback) { _movedCallback = callback; }
	void InvokeMoved(int x, int y) { if (_movedCallback) _movedCallback(x, y); }
	void SetTopmost(bool topmost);
	void SetIconFile(AutoString filename);
};

#endif // !WEBWINDOW_H
