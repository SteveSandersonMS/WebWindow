#ifdef OS_MAC
#include "WebWindow.h"
#import "WebWindow.Mac.AppDelegate.h"
#import "WebWindow.Mac.UiDelegate.h"
#import "WebWindow.Mac.UrlSchemeHandler.h"
#include <stdio.h>
#import <Cocoa/Cocoa.h>
#import <WebKit/WebKit.h>

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

WebWindow::WebWindow(UTF8String title, WebWindow* parent, WebMessageReceivedCallback webMessageReceivedCallback)
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

void WebWindow::AttachWebView()
{
    MyUiDelegate *uiDelegate = [[[MyUiDelegate alloc] init] autorelease];

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

void WebWindow::SetTitle(UTF8String title)
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

void WebWindow::ShowMessage(UTF8String title, UTF8String body, unsigned int type)
{
    NSString* nstitle = [[NSString stringWithUTF8String:title] autorelease];
    NSString* nsbody= [[NSString stringWithUTF8String:body] autorelease];
    NSAlert *alert = [[[NSAlert alloc] init] autorelease];
    [[alert window] setTitle:nstitle];
    [alert setMessageText:nsbody];
    [alert runModal];
}

void WebWindow::NavigateToString(UTF8String content)
{
    WKWebView *webView = (WKWebView *)_webview;
    NSString* nscontent = [[NSString stringWithUTF8String:content] autorelease];
    [webView loadHTMLString:nscontent baseURL:nil];
}

void WebWindow::NavigateToUrl(UTF8String url)
{
    WKWebView *webView = (WKWebView *)_webview;
    NSString* nsurlstring = [[NSString stringWithUTF8String:url] autorelease];
    NSURL *nsurl= [[NSURL URLWithString:nsurlstring] autorelease];
    NSURLRequest *nsrequest= [[NSURLRequest requestWithURL:nsurl] autorelease];
    [webView loadRequest:nsrequest];
}

void WebWindow::SendMessage(UTF8String message)
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

void WebWindow::AddCustomScheme(UTF8String scheme, WebResourceRequestedCallback requestHandler)
{
    // Note that this can only be done *before* the WKWebView is instantiated, so we only let this
    // get called from the options callback in the constructor
    MyUrlSchemeHandler* schemeHandler = [[[MyUrlSchemeHandler alloc] init] autorelease];
    schemeHandler->requestHandler = requestHandler;

    WKWebViewConfiguration *webviewConfiguration = (WKWebViewConfiguration *)_webviewConfiguration;
    NSString* nsscheme = [NSString stringWithUTF8String:scheme];
    [webviewConfiguration setURLSchemeHandler:schemeHandler forURLScheme:nsscheme];
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
    frame.origin.x -= frame.size.width;
    frame.origin.x += fw;
    frame.origin.y -= frame.size.height;
    frame.origin.y += fh;
    frame.size = CGSizeMake(fw, fh);
    [window setFrame: frame display: YES];
}

void WebWindow::GetScreenSize(int* width, int* height)
{
    NSWindow* window = (NSWindow*)_window;
    NSSize size = [[window screen] frame].size;
    if (width) *width = (int)roundf(size.width);
    if (height) *height = (int)roundf(size.height);
}

unsigned int WebWindow::GetScreenDpi()
{
	return 72;
}

void WebWindow::GetPosition(int* x, int* y)
{
    NSWindow* window = (NSWindow*)_window;
    NSRect frame = [window frame];
    if (x) *x = (int)roundf(frame.origin.x - frame.size.width);
    if (y) *y = (int)roundf(frame.origin.y - frame.size.height);
}

void WebWindow::SetPosition(int x, int y)
{
    NSWindow* window = (NSWindow*)_window;
    NSRect frame = [window frame];
    frame.origin.x = frame.size.width + (CGFloat)x;
    frame.origin.y = frame.size.height + (CGFloat)y;
    [window setFrame: frame display: YES];
}

#endif
