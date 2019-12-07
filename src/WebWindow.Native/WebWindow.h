#ifndef WEBWINDOW_H
#define WEBWINDOW_H

#ifdef _WIN32
#include <Windows.h>
#include <wrl/event.h>
#include <map>
#include <string>
#include <wil/com.h>
#include <WebView2.h>
#define WEBWINDOW_STDCALL __stdcall
#else
#ifdef OS_LINUX
#include <gtk/gtk.h>
#endif
#define WEBWINDOW_STDCALL
#endif

typedef char* UTF8String;

typedef void (WEBWINDOW_STDCALL* ACTION)();
typedef void (WEBWINDOW_STDCALL* WebMessageReceivedCallback)(UTF8String message);
typedef void* (WEBWINDOW_STDCALL* WebResourceRequestedCallback)(UTF8String url, int* outNumBytes, UTF8String* outContentType);

class WebWindow
{
private:
	WebMessageReceivedCallback _webMessageReceivedCallback;
#ifdef _WIN32
	static HINSTANCE _hInstance;
	HWND _hWnd;
	WebWindow* _parent;
	wil::com_ptr<IWebView2Environment> _webviewEnvironment;
	wil::com_ptr<IWebView2WebView> _webviewWindow;
	std::map<std::string, WebResourceRequestedCallback> _schemeToRequestHandler;
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

	WebWindow(UTF8String title, WebWindow* parent, WebMessageReceivedCallback webMessageReceivedCallback);
	~WebWindow();
	void SetTitle(UTF8String title);
	void Show();
	void WaitForExit();
	void ShowMessage(UTF8String title, UTF8String body, unsigned int type);
	void Invoke(ACTION callback);
	void NavigateToUrl(UTF8String url);
	void NavigateToString(UTF8String content);
	void SendMessage(UTF8String message);
	void AddCustomScheme(UTF8String scheme, WebResourceRequestedCallback requestHandler);
	void GetSize(int* width, int* height);
	void SetSize(int width, int height);
	void GetScreenSize(int* width, int* height);
	unsigned int GetScreenDpi();
	void GetPosition(int* x, int* y);
	void SetPosition(int x, int y);
	void SetTopmost(bool topmost);
};

#endif // !WEBWINDOW_H
