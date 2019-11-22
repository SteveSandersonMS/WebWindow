#include "WebWindow.h"
#define WIN32_LEAN_AND_MEAN             // Exclude rarely-used stuff from Windows headers
#include <windows.h>

#include <windows.web.ui.h>
#include <windows.web.ui.interop.h>
#include <windows.foundation.h>
#include <windows.foundation.collections.h>
#include <wrl.h>
#include <wrl/wrappers/corewrappers.h>
#include <stdio.h>
#include <map>
#include <mutex>
#include <condition_variable>
#include <comdef.h>
#include <atomic>
#include <Shlwapi.h>
#include "IAsyncOperationHelper.h"
#include <strsafe.h>

#define WM_USER_SHOWMESSAGE (WM_USER + 0x0001)
#define WM_USER_INVOKE (WM_USER + 0x0002)

using namespace ABI::Windows::Foundation;
using namespace ABI::Windows::Storage::Streams;
using namespace ABI::Windows::Web::UI;
using namespace ABI::Windows::Web::UI::Interop;
using namespace Microsoft::WRL;
using namespace Microsoft::WRL::Wrappers;

LRESULT CALLBACK WindowProc(HWND hwnd, UINT uMsg, WPARAM wParam, LPARAM lParam);
LPCWSTR CLASS_NAME = L"WebWindow";
std::mutex invokeLockMutex;
HINSTANCE WebWindow::_hInstance;
HWND messageLoopRootWindowHandle;
std::map<HWND, WebWindow*> hwndToWebWindow;

struct InvokeWaitInfo
{
	std::condition_variable completionNotifier;
	bool isCompleted;
};

struct ShowMessageParams
{
	std::wstring title;
	std::wstring body;
	UINT type;
};

void CheckFailure(_In_ HRESULT hr)
{
	if (FAILED(hr))
	{
		WCHAR message[512] = L"";
		StringCchPrintf(message, ARRAYSIZE(message), L"Error: 0x%x", hr);
		MessageBoxW(nullptr, message, nullptr, MB_OK);
		ExitProcess(-1);
	}
}

template <typename TInterface>
Microsoft::WRL::ComPtr<TInterface> GetActivationFactoryFailFast(_In_z_ PCWSTR factoryClassName)
{
	ComPtr<TInterface> factoryInstance;
	CheckFailure(RoGetActivationFactory(
		HStringReference(factoryClassName).Get(),
		IID_PPV_ARGS(&factoryInstance)));
	return factoryInstance;
}

template <typename TInterface>
Microsoft::WRL::ComPtr<TInterface> ActivateInstanceFailFast(_In_z_ PCWSTR className)
{
	ComPtr<TInterface> classInstanceAsInspectable;
	ComPtr<TInterface> classInstance;
	CheckFailure(RoActivateInstance(
		HStringReference(className).Get(),
		&classInstanceAsInspectable));
	CheckFailure(classInstanceAsInspectable.As(&classInstance));
	return classInstance;
}

ComPtr<IUriRuntimeClass> CreateWinRtUri(_In_z_ PCWSTR uri, _In_ bool allowInvalidUri = false)
{
	auto uriRuntimeClassFactory = GetActivationFactoryFailFast<IUriRuntimeClassFactory>(RuntimeClass_Windows_Foundation_Uri);
	ComPtr<IUriRuntimeClass> uriRuntimeClass;
	if (!allowInvalidUri)
	{
		CheckFailure(uriRuntimeClassFactory->CreateUri(HStringReference(uri).Get(), &uriRuntimeClass));
	}
	else
	{
		uriRuntimeClassFactory->CreateUri(HStringReference(uri).Get(), &uriRuntimeClass);
	}
	return uriRuntimeClass;
}

Rect HwndWindowRectToBoundsRect(_In_ HWND hwnd)
{
	RECT windowRect = { 0 };
	GetWindowRect(hwnd, &windowRect);

	Rect bounds =
	{
		0,
		0,
		static_cast<float>(windowRect.right - windowRect.left),
		static_cast<float>(windowRect.bottom - windowRect.top)
	};

	return bounds;
}

void WebWindow::Register(HINSTANCE hInstance)
{
	_hInstance = hInstance;

	// Register the window class	
	WNDCLASSW wc = { };
	wc.lpfnWndProc = WindowProc;
	wc.hInstance = hInstance;
	wc.lpszClassName = CLASS_NAME;
	RegisterClass(&wc);

	SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE);
}

WebWindow::WebWindow(AutoString title, WebWindow* parent, WebMessageReceivedCallback webMessageReceivedCallback)
{
	// Create the window
	_webMessageReceivedCallback = webMessageReceivedCallback;
	_parent = parent;
	_hWnd = CreateWindowEx(
		0,                              // Optional window styles.
		CLASS_NAME,                     // Window class
		title,							// Window text
		WS_OVERLAPPEDWINDOW,            // Window style

		// Size and position
		CW_USEDEFAULT, CW_USEDEFAULT, CW_USEDEFAULT, CW_USEDEFAULT,

		parent ? parent->_hWnd : NULL,       // Parent window
		NULL,       // Menu
		_hInstance, // Instance handle
		this        // Additional application data
	);
	hwndToWebWindow[_hWnd] = this;
}

// Needn't to release the handles.
WebWindow::~WebWindow() {}

HWND WebWindow::getHwnd()
{
	return _hWnd;
}

LRESULT CALLBACK WindowProc(HWND hwnd, UINT uMsg, WPARAM wParam, LPARAM lParam)
{
	switch (uMsg)
	{
	case WM_DESTROY:
	{
		// Only terminate the message loop if the window being closed is the one that
		// started the message loop
		hwndToWebWindow.erase(hwnd);
		if (hwnd == messageLoopRootWindowHandle)
		{
			PostQuitMessage(0);
		}
		return 0;
	}
	case WM_USER_SHOWMESSAGE:
	{
		ShowMessageParams* params = (ShowMessageParams*)wParam;
		MessageBox(hwnd, params->body.c_str(), params->title.c_str(), params->type);
		delete params;
		return 0;
	}

	case WM_USER_INVOKE:
	{
		ACTION callback = (ACTION)wParam;
		callback();
		InvokeWaitInfo* waitInfo = (InvokeWaitInfo*)lParam;
		{
			std::lock_guard<std::mutex> guard(invokeLockMutex);
			waitInfo->isCompleted = true;
		}
		waitInfo->completionNotifier.notify_one();
		return 0;
	}
	case WM_SIZE:
	{
		WebWindow* webWindow = hwndToWebWindow[hwnd];
		if (webWindow)
		{
			webWindow->RefitContent();
			int width, height;
			webWindow->GetSize(&width, &height);
			webWindow->InvokeResized(width, height);
		}
		else {

		}
		return 0;
	}
	case WM_MOVE:
	{
		WebWindow* webWindow = hwndToWebWindow[hwnd];
		if (webWindow)
		{
			int x, y;
			webWindow->GetPosition(&x, &y);
			webWindow->InvokeMoved(x, y);
		}
		return 0;
	}
	break;
	}

	return DefWindowProc(hwnd, uMsg, wParam, lParam);
}

void WebWindow::RefitContent()
{
	if (_webviewWindow)
	{
		RECT bounds;
		GetClientRect(_hWnd, &bounds);
		_webviewWindow->put_Bounds(bounds);
	}
	else {
		if (m_webViewControl)
		{
			RECT hwndBounds = { 0 };
			GetClientRect(_hWnd, &hwndBounds);
			const int clientWidth = hwndBounds.right - hwndBounds.left;
			const int clientHeight = hwndBounds.bottom - hwndBounds.top;

			SetWindowPos(_hWnd, nullptr, 0, 0, clientWidth, clientHeight - 0, SWP_NOZORDER);

			Rect bounds = HwndWindowRectToBoundsRect(_hWnd);
			ComPtr<IWebViewControlSite> site;
			CheckFailure(m_webViewControl.As(&site));
			CheckFailure(site->put_Bounds(bounds));
		}
	}
}

void WebWindow::SetTitle(AutoString title)
{
	SetWindowText(_hWnd, title);
}

void WebWindow::Show()
{
	ShowWindow(_hWnd, SW_SHOWDEFAULT);

	// Strangely, it only works to create the webview2 *after* the window has been shown,
	// so defer it until here. This unfortunately means you can't call the Navigate methods
	// until the window is shown.
	if (!_webviewWindow)
	{
		AttachWebView();
	}
}

void WebWindow::WaitForExit()
{
	messageLoopRootWindowHandle = _hWnd;

	// Run the message loop
	MSG msg = { };
	while (GetMessage(&msg, NULL, 0, 0))
	{
		TranslateMessage(&msg);
		DispatchMessage(&msg);
	}
}

void WebWindow::ShowMessage(AutoString title, AutoString body, UINT type)
{
	ShowMessageParams* params = new ShowMessageParams;
	params->title = title;
	params->body = body;
	params->type = type;
	PostMessage(_hWnd, WM_USER_SHOWMESSAGE, (WPARAM)params, 0);
}

void WebWindow::Invoke(ACTION callback)
{
	InvokeWaitInfo waitInfo = {};
	PostMessage(_hWnd, WM_USER_INVOKE, (WPARAM)callback, (LPARAM)&waitInfo);

	// Block until the callback is actually executed and completed
	// TODO: Add return values, exception handling, etc.
	std::unique_lock<std::mutex> uLock(invokeLockMutex);
	waitInfo.completionNotifier.wait(uLock, [&] { return waitInfo.isCompleted; });
}

bool WebWindow::AttachWebViewChromium()
{
	std::atomic_flag flag = ATOMIC_FLAG_INIT;
	flag.test_and_set();

	HRESULT envResult = CreateWebView2EnvironmentWithDetails(nullptr, nullptr, nullptr,
		Callback<IWebView2CreateWebView2EnvironmentCompletedHandler>(
			[&, this](HRESULT result, IWebView2Environment* env) -> HRESULT {
				HRESULT envResult = env->QueryInterface(&_webviewEnvironment);
				if (envResult != S_OK)
				{
					return envResult;
				}

				// Create a WebView, whose parent is the main window hWnd
				env->CreateWebView(_hWnd, Callback<IWebView2CreateWebViewCompletedHandler>(
					[&, this](HRESULT result, IWebView2WebView* webview) -> HRESULT {
						if (result != S_OK) { return result; }
						result = webview->QueryInterface(&_webviewWindow);
						if (result != S_OK) { return result; }

						// Add a few settings for the webview
						// this is a redundant demo step as they are the default settings values
						IWebView2Settings* Settings;
						_webviewWindow->get_Settings(&Settings);
						Settings->put_IsScriptEnabled(TRUE);
						Settings->put_AreDefaultScriptDialogsEnabled(TRUE);
						Settings->put_IsWebMessageEnabled(TRUE);

						// Register interop APIs
						EventRegistrationToken webMessageToken;
						_webviewWindow->AddScriptToExecuteOnDocumentCreated(L"window.external = { sendMessage: function(message) { window.chrome.webview.postMessage(message); }, receiveMessage: function(callback) { window.chrome.webview.addEventListener(\'message\', function(e) { callback(e.data); }); } };", nullptr);
						_webviewWindow->add_WebMessageReceived(Callback<IWebView2WebMessageReceivedEventHandler>(
							[this](IWebView2WebView* webview, IWebView2WebMessageReceivedEventArgs* args) -> HRESULT {
								wil::unique_cotaskmem_string message;
								args->get_WebMessageAsString(&message);
								_webMessageReceivedCallback(message.get());
								return S_OK;
							}).Get(), &webMessageToken);

						EventRegistrationToken webResourceRequestedToken;
						_webviewWindow->AddWebResourceRequestedFilter(L"*", WEBVIEW2_WEB_RESOURCE_CONTEXT_ALL);
						_webviewWindow->add_WebResourceRequested(Callback<IWebView2WebResourceRequestedEventHandler>(
							[this](IWebView2WebView* sender, IWebView2WebResourceRequestedEventArgs* args)
							{
								IWebView2WebResourceRequest* req;
								args->get_Request(&req);

								wil::unique_cotaskmem_string uri;
								req->get_Uri(&uri);
								std::wstring uriString = uri.get();
								size_t colonPos = uriString.find(L':', 0);
								if (colonPos > 0)
								{
									std::wstring scheme = uriString.substr(0, colonPos);
									WebResourceRequestedCallback handler = _schemeToRequestHandler[scheme];
									if (handler != NULL)
									{
										int numBytes;
										AutoString contentType;
										wil::unique_cotaskmem dotNetResponse(handler(uriString.c_str(), &numBytes, &contentType));

										if (dotNetResponse != nullptr && contentType != nullptr)
										{
											std::wstring contentTypeWS = contentType;

											IStream* dataStream = SHCreateMemStream((BYTE*)dotNetResponse.get(), numBytes);
											wil::com_ptr<IWebView2WebResourceResponse> response;
											_webviewEnvironment->CreateWebResourceResponse(
												dataStream, 200, L"OK", (L"Content-Type: " + contentTypeWS).c_str(),
												&response);
											args->put_Response(response.get());
										}
									}
								}

								return S_OK;
							}
						).Get(), &webResourceRequestedToken);

						RefitContent();

						flag.clear();
						return S_OK;
					}).Get());
				return S_OK;
			}).Get());

	if (envResult != S_OK)
	{
		return false;
	}

	// Block until it's ready. This simplifies things for the caller, so they
	// don't need to regard this process as async.
	MSG msg = { };
	while (flag.test_and_set() && GetMessage(&msg, NULL, 0, 0))
	{
		TranslateMessage(&msg);
		DispatchMessage(&msg);
	}

	return true;
}

bool WebWindow::AttachWebViewEdge()
{
	// winrt::init_apartment(winrt::apartment_type::single_threaded);
	HRESULT envResult = RoInitialize(RO_INIT_SINGLETHREADED);

	// Use default options if options weren't set on the App during App::RunNewInstance
	if (!m_processOptions)
	{
		m_processOptions = ActivateInstanceFailFast<IWebViewControlProcessOptions>(RuntimeClass_Windows_Web_UI_Interop_WebViewControlProcessOptions);
	}

	// Use a default new process if one wasn't set on the App during App::RunNewInstance
	if (!m_process)
	{
		ComPtr<IWebViewControlProcessFactory> webViewControlProcessFactory = GetActivationFactoryFailFast<IWebViewControlProcessFactory>(RuntimeClass_Windows_Web_UI_Interop_WebViewControlProcess);
		CheckFailure(webViewControlProcessFactory->CreateWithOptions(m_processOptions.Get(), &m_process));
	}

	ComPtr<IAsyncOperation<WebViewControl*>> createWebViewAsyncOperation;
	CheckFailure(m_process->CreateWebViewControlAsync(
		reinterpret_cast<INT64>(_hWnd),
		HwndWindowRectToBoundsRect(_hWnd),
		&createWebViewAsyncOperation));

	CheckFailure(AsyncOpHelpers::WaitForCompletionAndGetResults(createWebViewAsyncOperation.Get(), m_webViewControl.ReleaseAndGetAddressOf()));

	EventRegistrationToken token = { 0 };
	HRESULT hr = m_webViewControl->add_ContentLoading(Callback<ITypedEventHandler<IWebViewControl*, WebViewControlContentLoadingEventArgs*>>(
		[this](IWebViewControl* webViewControl, IWebViewControlContentLoadingEventArgs* args) -> HRESULT
		{
			ComPtr<IUriRuntimeClass> uri;
			CheckFailure(args->get_Uri(&uri));
			HString uriAsHString;
			CheckFailure(uri->get_AbsoluteUri(uriAsHString.ReleaseAndGetAddressOf()));
			//SetWindowText(m_addressbarWindow, uriAsHString.GetRawBuffer(nullptr));
			return S_OK;
		}).Get(), &token);
	CheckFailure(hr);

	//_com_error err(envResult);
	//LPCTSTR errMsg = err.ErrorMessage();
	//MessageBox(_hWnd, errMsg, L"Error instantiating webview", MB_OK);

	return envResult == S_OK;
}

void WebWindow::AttachWebView()
{
	if (AttachWebViewChromium())
	{
		return;
	}

	// Fallback to Edge rendering.
	AttachWebViewEdge();
}

void WebWindow::NavigateToUrl(AutoString url)
{
	if (_webviewWindow)
	{
		_webviewWindow->Navigate(url);
	}
	else
	{
		ComPtr<IUriRuntimeClass> uri = CreateWinRtUri(url, true);
		if (uri != nullptr)
		{
			//m_webViewControl->Navigate(uri.Get());
			CheckFailure(m_webViewControl->Navigate(uri.Get()));
		}
	}
}

void WebWindow::NavigateToString(AutoString content)
{
	_webviewWindow->NavigateToString(content);
}

void WebWindow::SendMessage(AutoString message)
{
	_webviewWindow->PostWebMessageAsString(message);
}

void WebWindow::AddCustomScheme(AutoString scheme, WebResourceRequestedCallback requestHandler)
{
	_schemeToRequestHandler[scheme] = requestHandler;
}

void WebWindow::SetResizable(bool resizable)
{
	LONG_PTR style = GetWindowLongPtr(_hWnd, GWL_STYLE);
	if (resizable) style |= WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX;
	else style &= (~WS_THICKFRAME) & (~WS_MINIMIZEBOX) & (~WS_MAXIMIZEBOX);
	SetWindowLongPtr(_hWnd, GWL_STYLE, style);
}

void WebWindow::GetSize(int* width, int* height)
{
	RECT rect = {};
	GetWindowRect(_hWnd, &rect);
	if (width) *width = rect.right - rect.left;
	if (height) *height = rect.bottom - rect.top;
}

void WebWindow::SetSize(int width, int height)
{
	SetWindowPos(_hWnd, HWND_TOP, 0, 0, width, height, SWP_NOMOVE | SWP_NOZORDER);
}

BOOL MonitorEnum(HMONITOR monitor, HDC, LPRECT, LPARAM arg)
{
	auto callback = (GetAllMonitorsCallback)arg;
	MONITORINFO info = {};
	info.cbSize = sizeof(MONITORINFO);
	GetMonitorInfo(monitor, &info);
	Monitor props = {};
	props.monitor.x = info.rcMonitor.left;
	props.monitor.y = info.rcMonitor.top;
	props.monitor.width = info.rcMonitor.right - info.rcMonitor.left;
	props.monitor.height = info.rcMonitor.bottom - info.rcMonitor.top;
	props.work.x = info.rcWork.left;
	props.work.y = info.rcWork.top;
	props.work.width = info.rcWork.right - info.rcWork.left;
	props.work.height = info.rcWork.bottom - info.rcWork.top;
	return callback(&props) ? TRUE : FALSE;
}

void WebWindow::GetAllMonitors(GetAllMonitorsCallback callback)
{
	if (callback)
	{
		EnumDisplayMonitors(NULL, NULL, MonitorEnum, (LPARAM)callback);
	}
}

unsigned int WebWindow::GetScreenDpi()
{
	return GetDpiForWindow(_hWnd);
}

void WebWindow::GetPosition(int* x, int* y)
{
	RECT rect = {};
	GetWindowRect(_hWnd, &rect);
	if (x) *x = rect.left;
	if (y) *y = rect.top;
}

void WebWindow::SetPosition(int x, int y)
{
	SetWindowPos(_hWnd, HWND_TOP, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER);
}

void WebWindow::SetTopmost(bool topmost)
{
	SetWindowPos(_hWnd, topmost ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
}

void WebWindow::SetIconFile(AutoString filename)
{
	HICON icon = (HICON)LoadImage(NULL, filename, IMAGE_ICON, 0, 0, LR_LOADFROMFILE);
	if (icon)
	{
		::SendMessage(_hWnd, WM_SETICON, ICON_SMALL, (LPARAM)icon);
	}
}
