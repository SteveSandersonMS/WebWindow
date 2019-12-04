#import <Cocoa/Cocoa.h>
#import <WebKit/WebKit.h>

typedef void* (*WebResourceRequestedCallback) (char* url, int* outNumBytes, char** outContentType);

@interface MyUrlSchemeHandler : NSObject <WKURLSchemeHandler> {
    @public
    WebResourceRequestedCallback requestHandler;
}
@end
