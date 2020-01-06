#import "WebWindow.Mac.NavigationDelegate.h"

@implementation MyNavigationDelegate : NSObject

- (void)webView:(WKWebView *)webView didStartProvisionalNavigation:(WKNavigation *)navigation;
{
    callUriChangedCallback([webView.URL.absoluteString UTF8String], uriChangedCallback);
}

- (void)webView:(WKWebView *)webView didFailNavigation:(WKNavigation *)navigation withError:(NSError *)error;
{
    callUriChangedCallback([webView.URL.absoluteString UTF8String], uriChangedCallback);
}

- (void)webView:(WKWebView *)webView
didReceiveServerRedirectForProvisionalNavigation:(WKNavigation *)navigation;
{
    callUriChangedCallback([webView.URL.absoluteString UTF8String], uriChangedCallback);
}

void callUriChangedCallback(const char* uri, UriChangedCallback* callback)
{
    int length = strlen(uri);
    char* uriWritable = new char[ length + 1]();
    strncpy(uriWritable, uri, length);
    callback(uriWritable);
}
@end
