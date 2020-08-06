#ifdef OS_MAC
#include "WebWindow.h"
#import "WebWindow.Mac.AppDelegate.h"
#import "WebWindow.Mac.UiDelegate.h"
#import "WebWindow.Mac.UrlSchemeHandler.h"
#include <cstdio>
#include <map>
#import <Cocoa/Cocoa.h>
#import <WebKit/WebKit.h>

using namespace std;

map<NSWindow*, WebWindow*> nsWindowToWebWindow;

void WebWindow::Register()
{
    [NSAutoreleasePool new];
    [NSApplication sharedApplication];
    [NSApp setActivationPolicy:NSApplicationActivationPolicyRegular];
    id menubar = [[NSMenu new] autorelease];
    id appMenuItem = [[NSMenuItem new] autorelease];
    [menubar addItem:appMenuItem];
    [NSApp setMainMenu:menubar];
    id appMenu = [[NSMenu new] autorelease];
    id appName = [[NSProcessInfo processInfo] processName];
    id quitTitle = [@"Quit " stringByAppendingString:appName];
    id quitMenuItem = [[[NSMenuItem alloc] initWithTitle:quitTitle
        action:@selector(terminate:) keyEquivalent:@"q"] autorelease];
    [appMenu addItem:quitMenuItem];
    [appMenuItem setSubmenu:appMenu];

    MyApplicationDelegate * appDelegate = [[[MyApplicationDelegate alloc] init] autorelease];
    NSApplication * application = [NSApplication sharedApplication];
    [application setDelegate:appDelegate];
}

WebWindow::WebWindow(AutoString title, WebWindow* parent, WebMessageReceivedCallback webMessageReceivedCallback)
{
    _webMessageReceivedCallback = webMessageReceivedCallback;
    NSRect frame = NSMakeRect(0, 0, 900, 600);
    NSWindow *window = [[NSWindow alloc]
        initWithContentRect:frame
        styleMask:NSWindowStyleMaskTitled | NSWindowStyleMaskClosable | NSWindowStyleMaskResizable | NSWindowStyleMaskMiniaturizable
        backing: NSBackingStoreBuffered
        defer: false];
    _window = window;

    [window cascadeTopLeftFromPoint:NSMakePoint(20,20)];
    SetTitle(title);

    WKWebViewConfiguration *webViewConfiguration = [[WKWebViewConfiguration alloc] init];
    [webViewConfiguration.preferences setValue:@YES forKey:@"developerExtrasEnabled"];
    _webviewConfiguration = webViewConfiguration;
    _webview = nil;
}

WebWindow::~WebWindow()
{
    WKWebViewConfiguration *webViewConfiguration = (WKWebViewConfiguration*)_webviewConfiguration;
    [webViewConfiguration release];
    WKWebView *webView = (WKWebView*)_webview;
    [webView release];
    NSWindow* window = (NSWindow*)_window;
    [window close];
}

void WebWindow::AttachWebView()
{
    MyUiDelegate *uiDelegate = [[[MyUiDelegate alloc] init] autorelease];
    uiDelegate->webWindow = this;

    NSString *initScriptSource = @"window.__receiveMessageCallbacks = [];"
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
			"};";
    WKUserScript *initScript = [[WKUserScript alloc] initWithSource:initScriptSource injectionTime:WKUserScriptInjectionTimeAtDocumentStart forMainFrameOnly:YES];
    WKUserContentController *userContentController = [WKUserContentController new];
    WKWebViewConfiguration *webviewConfiguration = (WKWebViewConfiguration *)_webviewConfiguration;
    webviewConfiguration.userContentController = userContentController;
    [userContentController addUserScript:initScript];

    NSWindow *window = (NSWindow*)_window;
    WKWebView *webView = [[WKWebView alloc] initWithFrame:window.contentView.frame configuration:webviewConfiguration];
    [webView setAutoresizingMask:NSViewWidthSizable | NSViewHeightSizable];
    [window.contentView addSubview:webView];
    [window.contentView setAutoresizesSubviews:YES];

    uiDelegate->window = window;
    webView.UIDelegate = uiDelegate;

    uiDelegate->webMessageReceivedCallback = _webMessageReceivedCallback;
    [userContentController addScriptMessageHandler:uiDelegate name:@"webwindowinterop"];

    // TODO: Remove these observers when the window is closed
    [[NSNotificationCenter defaultCenter] addObserver:uiDelegate selector:@selector(windowDidResize:) name:NSWindowDidResizeNotification object:window];
    [[NSNotificationCenter defaultCenter] addObserver:uiDelegate selector:@selector(windowDidMove:) name:NSWindowDidMoveNotification object:window];

    _webview = webView;
}

void WebWindow::Show()
{
    if (_webview == nil) {
        AttachWebView();
    }

    NSWindow * window = (NSWindow*)_window;
    [window makeKeyAndOrderFront:nil];
}

void WebWindow::SetTitle(AutoString title)
{
    NSWindow* window = (NSWindow*)_window;
    NSString* nstitle = [[NSString stringWithUTF8String:title] autorelease];
    [window setTitle:nstitle];
}

void WebWindow::WaitForExit()
{
    [NSApp run];
}

void WebWindow::Invoke(ACTION callback)
{
    dispatch_sync(dispatch_get_main_queue(), ^(void){
        callback();
    });
}
void EnsureInvoke(dispatch_block_t block)
{
    if ([NSThread isMainThread])
    {
        block();
    }
    else
    {
        dispatch_async(dispatch_get_main_queue(), block);
    }
}

void WebWindow::ShowMessage(AutoString title, AutoString body, unsigned int type)
{

    EnsureInvoke(^{
        NSString* nstitle = [[NSString stringWithUTF8String:title] autorelease];
        NSString* nsbody= [[NSString stringWithUTF8String:body] autorelease];
        NSAlert *alert = [[[NSAlert alloc] init] autorelease];
        [[alert window] setTitle:nstitle];
        [alert setMessageText:nsbody];
        [alert runModal];
    });
}

void WebWindow::NavigateToString(AutoString content)
{
    WKWebView *webView = (WKWebView *)_webview;
    NSString* nscontent = [[NSString stringWithUTF8String:content] autorelease];
    [webView loadHTMLString:nscontent baseURL:nil];
}

void WebWindow::NavigateToUrl(AutoString url)
{
    WKWebView *webView = (WKWebView *)_webview;
    NSString* nsurlstring = [[NSString stringWithUTF8String:url] autorelease];
    NSURL *nsurl= [[NSURL URLWithString:nsurlstring] autorelease];
    NSURLRequest *nsrequest= [[NSURLRequest requestWithURL:nsurl] autorelease];
    [webView loadRequest:nsrequest];
}

void WebWindow::SendMessage(AutoString message)
{
    // JSON-encode the message
    NSString* nsmessage = [NSString stringWithUTF8String:message];
    NSData* data = [NSJSONSerialization dataWithJSONObject:@[nsmessage] options:0 error:nil];
    NSString *nsmessageJson = [[[NSString alloc]
        initWithData:data
        encoding:NSUTF8StringEncoding] autorelease];
    nsmessageJson = [[nsmessageJson substringToIndex:([nsmessageJson length]-1)] substringFromIndex:1];

    WKWebView *webView = (WKWebView *)_webview;
    NSString *javaScriptToEval = [NSString stringWithFormat:@"__dispatchMessageCallback(%@)", nsmessageJson];
    [webView evaluateJavaScript:javaScriptToEval completionHandler:nil];
}

void WebWindow::AddCustomScheme(AutoString scheme, WebResourceRequestedCallback requestHandler)
{
    // Note that this can only be done *before* the WKWebView is instantiated, so we only let this
    // get called from the options callback in the constructor
    MyUrlSchemeHandler* schemeHandler = [[[MyUrlSchemeHandler alloc] init] autorelease];
    schemeHandler->requestHandler = requestHandler;

    WKWebViewConfiguration *webviewConfiguration = (WKWebViewConfiguration *)_webviewConfiguration;
    NSString* nsscheme = [NSString stringWithUTF8String:scheme];
    [webviewConfiguration setURLSchemeHandler:schemeHandler forURLScheme:nsscheme];
}

void WebWindow::SetResizable(bool resizable)
{
    NSWindow* window = (NSWindow*)_window;
    if (resizable) window.styleMask |= NSWindowStyleMaskResizable;
    else window.styleMask &= ~NSWindowStyleMaskResizable;
}

void WebWindow::GetSize(int* width, int* height)
{
    NSWindow* window = (NSWindow*)_window;
    NSSize size = [window frame].size;
    if (width) *width = (int)roundf(size.width);
    if (height) *height = (int)roundf(size.height);
}

void WebWindow::SetSize(int width, int height)
{
    CGFloat fw = (CGFloat)width;
    CGFloat fh = (CGFloat)height;
    NSWindow* window = (NSWindow*)_window;
    NSRect frame = [window frame];
    CGFloat oldHeight = frame.size.height;
    CGFloat heightDelta = fh - oldHeight;  
    frame.size = CGSizeMake(fw, fh);
    frame.origin.y -= heightDelta;
    [window setFrame: frame display: YES];
}

void WebWindow::GetAllMonitors(GetAllMonitorsCallback callback)
{
    if (callback)
    {
        for (NSScreen* screen in [NSScreen screens])
        {
            Monitor props = {};
            NSRect frame = [screen frame];
            props.monitor.x = (int)roundf(frame.origin.x);
            props.monitor.y = (int)roundf(frame.origin.y);
            props.monitor.width = (int)roundf(frame.size.width);
            props.monitor.height = (int)roundf(frame.size.height);
            NSRect vframe = [screen visibleFrame];
            props.work.x = (int)roundf(vframe.origin.x);
            props.work.y = (int)roundf(vframe.origin.y);
            props.work.width = (int)roundf(vframe.size.width);
            props.work.height = (int)roundf(vframe.size.height);
            callback(&props);
        }
    }
}

unsigned int WebWindow::GetScreenDpi()
{
	return 72;
}

void WebWindow::GetPosition(int* x, int* y)
{
    NSWindow* window = (NSWindow*)_window;
    NSRect frame = [window frame];
    if (x) *x = (int)roundf(frame.origin.x);
    if (y) *y = (int)roundf(-frame.size.height - frame.origin.y); // It will be negative, because macOS measures from bottom-left. For x-plat consistency, we want increasing this value to mean moving down.
}

void WebWindow::SetPosition(int x, int y)
{
    NSWindow* window = (NSWindow*)_window;
    NSRect frame = [window frame];
    frame.origin.x = (CGFloat)x;
    frame.origin.y = -frame.size.height - (CGFloat)y;
    [window setFrame: frame display: YES];
}

void WebWindow::SetTopmost(bool topmost)
{
    NSWindow* window = (NSWindow*)_window;
    if (topmost) [window setLevel:NSFloatingWindowLevel];
    else [window setLevel:NSNormalWindowLevel];
}

void WebWindow::SetIconFile(AutoString filename)
{
	NSString* path = [[NSString stringWithUTF8String:filename] autorelease];
    NSImage* icon = [[NSImage alloc] initWithContentsOfFile:path];
    if (icon != nil)
    {
        NSWindow* window = (NSWindow*)_window;
        [[window standardWindowButton:NSWindowDocumentIconButton] setImage:icon];
    }
}

#endif
