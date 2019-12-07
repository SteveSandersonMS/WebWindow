#include "WebWindow.h"

#ifdef _WIN32
# define EXPORTED __declspec(dllexport)
#else
# define EXPORTED
#endif

extern "C"
{
#ifdef _WIN32
	EXPORTED void WebWindow_register_win32(HINSTANCE hInstance)
	{
		WebWindow::Register(hInstance);
	}

	EXPORTED HWND WebWindow_getHwnd_win32(WebWindow* instance)
	{
		return instance->getHwnd();
	}
#elif OS_MAC
	EXPORTED void WebWindow_register_mac()
	{
		WebWindow::Register();
	}
#endif

	EXPORTED WebWindow* WebWindow_ctor(UTF8String title, WebWindow* parent, WebMessageReceivedCallback webMessageReceivedCallback)
	{
		return new WebWindow(title, parent, webMessageReceivedCallback);
	}

	EXPORTED void WebWindow_dtor(WebWindow* instance)
	{
		delete instance;
	}

	EXPORTED void WebWindow_SetTitle(WebWindow* instance, UTF8String title)
	{
		instance->SetTitle(title);
	}

	EXPORTED void WebWindow_Show(WebWindow* instance)
	{
		instance->Show();
	}

	EXPORTED void WebWindow_WaitForExit(WebWindow* instance)
	{
		instance->WaitForExit();
	}

	EXPORTED void WebWindow_ShowMessage(WebWindow* instance, UTF8String title, UTF8String body, unsigned int type)
	{
		instance->ShowMessage(title, body, type);
	}

	EXPORTED void WebWindow_Invoke(WebWindow* instance, ACTION callback)
	{
		instance->Invoke(callback);
	}

	EXPORTED void WebWindow_NavigateToString(WebWindow* instance, UTF8String content)
	{
		instance->NavigateToString(content);
	}

	EXPORTED void WebWindow_NavigateToUrl(WebWindow* instance, UTF8String url)
	{
		instance->NavigateToUrl(url);
	}

	EXPORTED void WebWindow_SendMessage(WebWindow* instance, UTF8String message)
	{
		instance->SendMessage(message);
	}

	EXPORTED void WebWindow_AddCustomScheme(WebWindow* instance, UTF8String scheme, WebResourceRequestedCallback requestHandler)
	{
		instance->AddCustomScheme(scheme, requestHandler);
	}

	EXPORTED void WebWindow_GetSize(WebWindow* instance, int* width, int* height)
	{
		instance->GetSize(width, height);
	}

	EXPORTED void WebWindow_SetSize(WebWindow* instance, int width, int height)
	{
		instance->SetSize(width, height);
	}

	EXPORTED void WebWindow_GetScreenSize(WebWindow* instance, int* width, int* height)
	{
		instance->GetScreenSize(width, height);
	}

	EXPORTED unsigned int WebWindow_GetScreenDpi(WebWindow* instance)
	{
		return instance->GetScreenDpi();
	}

	EXPORTED void WebWindow_GetPosition(WebWindow* instance, int* x, int* y)
	{
		instance->GetPosition(x, y);
	}

	EXPORTED void WebWindow_SetPosition(WebWindow* instance, int x, int y)
	{
		instance->SetPosition(x, y);
	}
}
