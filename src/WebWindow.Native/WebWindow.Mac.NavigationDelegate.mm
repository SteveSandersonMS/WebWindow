#import "WebWindow.Mac.NavigationDelegate.h"

@implementation MyNavigationDelegate : NSObject

- (void)webView:(WKWebView *)webView didStartProvisionalNavigation:(WKNavigation *)navigation;
{
    callUriChangedCallback(webView);
}

- (void)webView:(WKWebView *)webView didFailNavigation:(WKNavigation *)navigation withError:(NSError *)error;
{
    callUriChangedCallback(webView);
}

- (void)webView:(WKWebView *)webView
didReceiveServerRedirectForProvisionalNavigation:(WKNavigation *)navigation;
{
    callUriChangedCallback(webView);
}

void callURIChangedCallback(WKWebView *webView)
{
    const char* uri = [webView.URL.absoluteString UTF8String];
    int length = strlen(uri);
    char* uriWritable = new char[ length + 1]();
    strncpy(uriWritable, uri, length);
    uriChangedCallback(uriWritable);
}
@end
