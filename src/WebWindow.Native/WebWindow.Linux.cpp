// For this to build on WSL (Ubuntu 18.04) you need to:
//  sudo apt-get install libgtk-3-dev libwebkit2gtk-4.0-dev
#ifdef OS_LINUX
#include "WebWindow.h"
#include <mutex>
#include <condition_variable>
#include <X11/Xlib.h>
#include <webkit2/webkit2.h>
#include <JavaScriptCore/JavaScript.h>
#include <sstream>
#include <iomanip>

std::mutex invokeLockMutex;

struct InvokeWaitInfo
{
	ACTION callback;
	std::condition_variable completionNotifier;
	bool isCompleted;
};

struct InvokeJSWaitInfo
{
	bool isCompleted;
};

void on_size_allocate(GtkWidget* widget, GdkRectangle* allocation, gpointer self);
gboolean on_configure_event(GtkWidget* widget, GdkEvent* event, gpointer self);

WebWindow::WebWindow(AutoString title, WebWindow* parent, WebMessageReceivedCallback webMessageReceivedCallback) : _webview(nullptr)
{
	_webMessageReceivedCallback = webMessageReceivedCallback;

	// It makes xlib thread safe.
	// Needed for get_position.
	XInitThreads();

	gtk_init(0, NULL);
	_window = gtk_window_new(GTK_WINDOW_TOPLEVEL);
	gtk_window_set_default_size(GTK_WINDOW(_window), 900, 600);
	SetTitle(title);

	if (parent == NULL)
	{
		g_signal_connect(G_OBJECT(_window), "destroy",
			G_CALLBACK(+[](GtkWidget* w, gpointer arg) { gtk_main_quit(); }),
			this);
		g_signal_connect(G_OBJECT(_window), "size-allocate",
			G_CALLBACK(on_size_allocate),
			this);
		g_signal_connect(G_OBJECT(_window), "configure-event",
			G_CALLBACK(on_configure_event),
			this);
	}
}

WebWindow::~WebWindow()
{
	gtk_widget_destroy(_window);
}

void HandleWebMessage(WebKitUserContentManager* contentManager, WebKitJavascriptResult* jsResult, gpointer arg)
{
	JSCValue* jsValue = webkit_javascript_result_get_js_value(jsResult);
	if (jsc_value_is_string(jsValue)) {
		AutoString str_value = jsc_value_to_string(jsValue);

		WebMessageReceivedCallback callback = (WebMessageReceivedCallback)arg;
		callback(str_value);
		g_free(str_value);
	}

	webkit_javascript_result_unref(jsResult);
}

void WebWindow::Show()
{
	if (!_webview)
	{
		WebKitUserContentManager* contentManager = webkit_user_content_manager_new();
		_webview = webkit_web_view_new_with_user_content_manager(contentManager);
		gtk_container_add(GTK_CONTAINER(_window), _webview);

		WebKitUserScript* script = webkit_user_script_new(
			"window.__receiveMessageCallbacks = [];"
			"window.__dispatchMessageCallback = function(message) {"
			"	window.__receiveMessageCallbacks.forEach(function(callback) { callback(message); });"
			"};"
			"window.external = {"
			"	sendMessage: function(message) {"
			"		window.webkit.messageHandlers.webwindowinterop.postMessage(message);"
			"	},"
			"	receiveMessage: function(callback) {"
			"		window.__receiveMessageCallbacks.push(callback);"
			"	}"
			"};", WEBKIT_USER_CONTENT_INJECT_ALL_FRAMES, WEBKIT_USER_SCRIPT_INJECT_AT_DOCUMENT_START, NULL, NULL);
		webkit_user_content_manager_add_script(contentManager, script);
		webkit_user_script_unref(script);

		g_signal_connect(contentManager, "script-message-received::webwindowinterop",
			G_CALLBACK(HandleWebMessage), (void*)_webMessageReceivedCallback);
		webkit_user_content_manager_register_script_message_handler(contentManager, "webwindowinterop");
	}

	gtk_widget_show_all(_window);

	WebKitWebInspector* inspector = webkit_web_view_get_inspector(WEBKIT_WEB_VIEW(_webview));
	webkit_web_inspector_show(WEBKIT_WEB_INSPECTOR(inspector));
}

void WebWindow::SetTitle(AutoString title)
{
	gtk_window_set_title(GTK_WINDOW(_window), title);
}

void WebWindow::WaitForExit()
{
	gtk_main();
}

static gboolean invokeCallback(gpointer data)
{
	InvokeWaitInfo* waitInfo = (InvokeWaitInfo*)data;
	waitInfo->callback();
	{
		std::lock_guard<std::mutex> guard(invokeLockMutex);
		waitInfo->isCompleted = true;
	}
	waitInfo->completionNotifier.notify_one();
	return false;
}

void WebWindow::Invoke(ACTION callback)
{
	InvokeWaitInfo waitInfo = { };
	waitInfo.callback = callback;
	gdk_threads_add_idle(invokeCallback, &waitInfo);

	// Block until the callback is actually executed and completed
	// TODO: Add return values, exception handling, etc.
	std::unique_lock<std::mutex> uLock(invokeLockMutex);
	waitInfo.completionNotifier.wait(uLock, [&] { return waitInfo.isCompleted; });
}

void WebWindow::ShowMessage(AutoString title, AutoString body, unsigned int type)
{
	GtkWidget* dialog = gtk_message_dialog_new(GTK_WINDOW(_window),
		GTK_DIALOG_DESTROY_WITH_PARENT,
		GTK_MESSAGE_OTHER,
		GTK_BUTTONS_OK,
		"%s",
		body);
	gtk_window_set_title((GtkWindow*)dialog, title);
	gtk_dialog_run(GTK_DIALOG(dialog));
	gtk_widget_destroy(dialog);
}

void WebWindow::NavigateToUrl(AutoString url)
{
	webkit_web_view_load_uri(WEBKIT_WEB_VIEW(_webview), url);
}

void WebWindow::NavigateToString(AutoString content)
{
	webkit_web_view_load_html(WEBKIT_WEB_VIEW(_webview), content, NULL);
}

// From https://stackoverflow.com/a/33799784
std::string escape_json(const std::string& s) {
	std::ostringstream o;
	for (auto c = s.cbegin(); c != s.cend(); c++) {
		switch (*c) {
		case '"': o << "\\\""; break;
		case '\\': o << "\\\\"; break;
		case '\b': o << "\\b"; break;
		case '\f': o << "\\f"; break;
		case '\n': o << "\\n"; break;
		case '\r': o << "\\r"; break;
		case '\t': o << "\\t"; break;
		default:
			if ('\x00' <= *c && *c <= '\x1f') {
				o << "\\u"
					<< std::hex << std::setw(4) << std::setfill('0') << (int)*c;
			}
			else {
				o << *c;
			}
		}
	}
	return o.str();
}

static void webview_eval_finished(GObject* object, GAsyncResult* result, gpointer userdata) {
	InvokeJSWaitInfo* waitInfo = (InvokeJSWaitInfo*)userdata;
	waitInfo->isCompleted = true;
}

void WebWindow::SendMessage(AutoString message)
{
	std::string js;
	js.append("__dispatchMessageCallback(\"");
	js.append(escape_json(message));
	js.append("\")");

	InvokeJSWaitInfo invokeJsWaitInfo = {};
	webkit_web_view_run_javascript(WEBKIT_WEB_VIEW(_webview),
		js.c_str(), NULL, webview_eval_finished, &invokeJsWaitInfo);
	while (!invokeJsWaitInfo.isCompleted) {
		g_main_context_iteration(NULL, TRUE);
	}
}

void HandleCustomSchemeRequest(WebKitURISchemeRequest* request, gpointer user_data)
{
	WebResourceRequestedCallback webResourceRequestedCallback = (WebResourceRequestedCallback)user_data;

	const gchar* uri = webkit_uri_scheme_request_get_uri(request);
	int numBytes;
	AutoString contentType;
	void* dotNetResponse = webResourceRequestedCallback((AutoString)uri, &numBytes, &contentType);
	GInputStream* stream = g_memory_input_stream_new_from_data(dotNetResponse, numBytes, NULL);
	webkit_uri_scheme_request_finish(request, (GInputStream*)stream, -1, contentType);
	g_object_unref(stream);
	delete[] contentType;
}

void WebWindow::AddCustomScheme(AutoString scheme, WebResourceRequestedCallback requestHandler)
{
	WebKitWebContext* context = webkit_web_context_get_default();
	webkit_web_context_register_uri_scheme(context, scheme,
		(WebKitURISchemeRequestCallback)HandleCustomSchemeRequest,
		(void*)requestHandler, NULL);
}

void WebWindow::SetResizable(bool resizable)
{
	gtk_window_set_resizable(GTK_WINDOW(_window), resizable ? TRUE : FALSE);
}

void WebWindow::GetSize(int* width, int* height)
{
	gtk_window_get_size(GTK_WINDOW(_window), width, height);
}

void WebWindow::SetSize(int width, int height)
{
	gtk_window_resize(GTK_WINDOW(_window), width, height);
}

void on_size_allocate(GtkWidget* widget, GdkRectangle* allocation, gpointer self)
{
	int width, height;
	gtk_window_get_size(GTK_WINDOW(widget), &width, &height);
	((WebWindow*)self)->InvokeResized(width, height);
}

void WebWindow::GetAllMonitors(GetAllMonitorsCallback callback)
{
    if (callback)
    {
        GdkScreen* screen = gtk_window_get_screen(GTK_WINDOW(_window));
        GdkDisplay* display = gdk_screen_get_display(screen);
        int n = gdk_display_get_n_monitors(display);
        for (int i = 0; i < n; i++)
        {
            GdkMonitor* monitor = gdk_display_get_monitor(display, i);
            Monitor props = {};
            gdk_monitor_get_geometry(monitor, (GdkRectangle*)&props.monitor);
            gdk_monitor_get_workarea(monitor, (GdkRectangle*)&props.work);
            if (!callback(&props)) break;
        }
    }
}

unsigned int WebWindow::GetScreenDpi()
{
	GdkScreen* screen = gtk_window_get_screen(GTK_WINDOW(_window));
	gdouble dpi = gdk_screen_get_resolution(screen);
	if (dpi < 0) return 96;
	else return (unsigned int)dpi;
}

void WebWindow::GetPosition(int* x, int* y)
{
	gtk_window_get_position(GTK_WINDOW(_window), x, y);
}

void WebWindow::SetPosition(int x, int y)
{
	gtk_window_move(GTK_WINDOW(_window), x, y);
}

gboolean on_configure_event(GtkWidget* widget, GdkEvent* event, gpointer self)
{
	if (event->type == GDK_CONFIGURE)
	{
		((WebWindow*)self)->InvokeMoved(event->configure.x, event->configure.y);
	}
	return FALSE;
}

void WebWindow::SetTopmost(bool topmost)
{
	gtk_window_set_keep_above(GTK_WINDOW(_window), topmost ? TRUE : FALSE);
}

void WebWindow::SetIconFile(AutoString filename)
{
	gtk_window_set_icon_from_file(GTK_WINDOW(_window), filename, NULL);
}

#endif
