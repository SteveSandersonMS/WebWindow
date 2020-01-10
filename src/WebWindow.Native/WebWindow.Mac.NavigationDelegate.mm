#import "WebWindow.Mac.NavigationDelegate.h"

@implementation MyNavigationDelegate : NSObject

- (void)webView:(WKWebView *)webView didStartProvisionalNavigation:(WKNavigation *)navigation;
{
    [self callUriChangedCallback:webView.URL.absoluteString];
}

- (void)webView:(WKWebView *)webView didFailNavigation:(WKNavigation *)navigation withError:(NSError *)error;
{
    [self callUriChangedCallback:webView.URL.absoluteString];
}

- (void)webView:(WKWebView *)webView
didReceiveServerRedirectForProvisionalNavigation:(WKNavigation *)navigation;
{
    [self callUriChangedCallback:webView.URL.absoluteString];
}

- (void) callUriChangedCallback: (NSString *) uri;
{
    char* writableUri = (char*)[uri UTF8String];
    uriChangedCallback(writableUri);
}
@end
