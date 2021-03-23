#import <Cocoa/Cocoa.h>
#import <WebKit/WebKit.h>
#include "WebWindow.h"

typedef void (*UriChangedCallback) (char* message);

@interface MyNavigationDelegate : NSObject <WKNavigationDelegate> {
    @public
    NSWindow * window;
    WebWindow * webWindow;
    UriChangedCallback uriChangedCallback;
}
@end