#pragma once
typedef char* UTF8String;

#ifdef _WIN32
#include <Windows.h>
#include <wrl\event.h>
#include <map>
#include <string>
#include <wil/com.h>
#include "WebView2.h"
typedef void(__stdcall* ACTION)();
typedef void(__stdcall* WebMessageReceivedCallback)(UTF8String message);
typedef void* (__stdcall *WebResourceRequestedCallback) (UTF8String url, int* outNumBytes, UTF8String *outContentType);
#else
	#ifdef OS_LINUX
	#include <gtk/gtk.h>
	#endif
typedef void (*ACTION) ();
typedef void (*WebMessageReceivedCallback) (UTF8String message);
typedef void* (*WebResourceRequestedCallback) (UTF8String url, int* outNumBytes, UTF8String* outContentType);
#endif

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
	void SetTitle(UTF8String title);
	void Show();
	void WaitForExit();
	void ShowMessage(UTF8String title, UTF8String body, unsigned int type);
	void Invoke(ACTION callback);
	void NavigateToUrl(UTF8String url);
	void NavigateToString(UTF8String content);
	void SendMessage(UTF8String message);
	void CloseWindow();
	void AddCustomScheme(UTF8String scheme, WebResourceRequestedCallback requestHandler);
};
