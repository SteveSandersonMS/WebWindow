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

	EXPORTED WebWindow* WebWindow_ctor(AutoString title, WebWindow* parent, WebMessageReceivedCallback webMessageReceivedCallback)
	{
		return new WebWindow(title, parent, webMessageReceivedCallback);
	}

	EXPORTED void WebWindow_dtor(WebWindow* instance)
	{
		delete instance;
	}

	EXPORTED void WebWindow_SetTitle(WebWindow* instance, AutoString title)
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

	EXPORTED void WebWindow_ShowMessage(WebWindow* instance, AutoString title, AutoString body, unsigned int type)
	{
		instance->ShowMessage(title, body, type);
	}

	EXPORTED void WebWindow_Invoke(WebWindow* instance, ACTION callback)
	{
		instance->Invoke(callback);
	}

	EXPORTED void WebWindow_NavigateToString(WebWindow* instance, AutoString content)
	{
		instance->NavigateToString(content);
	}

	EXPORTED void WebWindow_NavigateToUrl(WebWindow* instance, AutoString url)
	{
		instance->NavigateToUrl(url);
	}

	EXPORTED void WebWindow_SendMessage(WebWindow* instance, AutoString message)
	{
		instance->SendMessage(message);
	}

	EXPORTED void WebWindow_AddCustomScheme(WebWindow* instance, AutoString scheme, WebResourceRequestedCallback requestHandler)
	{
		instance->AddCustomScheme(scheme, requestHandler);
	}

	EXPORTED void WebWindow_SetResizable(WebWindow* instance, int resizable)
	{
		instance->SetResizable(resizable);
	}

	EXPORTED void WebWindow_GetSize(WebWindow* instance, int* width, int* height)
	{
		instance->GetSize(width, height);
	}

	EXPORTED void WebWindow_SetSize(WebWindow* instance, int width, int height)
	{
		instance->SetSize(width, height);
	}

	EXPORTED void WebWindow_SetResizedCallback(WebWindow* instance, ResizedCallback callback)
	{
		instance->SetResizedCallback(callback);
	}

	EXPORTED void WebWindow_GetAllMonitors(WebWindow* instance, GetAllMonitorsCallback callback)
	{
		instance->GetAllMonitors(callback);
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

	EXPORTED void WebWindow_SetMovedCallback(WebWindow* instance, MovedCallback callback)
	{
		instance->SetMovedCallback(callback);
	}

	EXPORTED void WebWindow_SetTopmost(WebWindow* instance, int topmost)
	{
		instance->SetTopmost(topmost);
	}

	EXPORTED void WebWindow_SetIconFile(WebWindow* instance, AutoString filename)
	{
		instance->SetIconFile(filename);
	}
}
