#import "WebWindow.Mac.NavigationDelegate.h"

@implementation MyNavigationDelegate : NSObject

- (void)webView:(WKWebView *)webView didStartProvisionalNavigation:(WKNavigation *)navigation;
{
    const char* uri = [webView.URL.absoluteString UTF8String];
    int length = strlen(uri);
    char* uriWritable = new char[length + 1]();
    strncpy(uriWritable, uri, length);
    uriChangedCallback(uriWritable);
}

- (void)webView:(WKWebView *)webView didFailNavigation:(WKNavigation *)navigation withError:(NSError *)error;
{
const char* uri = [webView.URL.absoluteString UTF8String];
int length = strlen(uri);
char* uriWritable = new char[ length + 1]();
strncpy(uriWritable, uri, length);
uriChangedCallback(uriWritable);
}

- (void)webView:(WKWebView *)webView
didReceiveServerRedirectForProvisionalNavigation:(WKNavigation *)navigation;
{
const char* uri = [webView.URL.absoluteString UTF8String];
int length = strlen(uri);
char* uriWritable = new char[ length + 1]();
strncpy(uriWritable, uri, length);
uriChangedCallback(uriWritable);
}
@end
