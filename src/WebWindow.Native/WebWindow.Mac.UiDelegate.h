#import <Cocoa/Cocoa.h>
#import <WebKit/WebKit.h>

typedef void (*WebMessageReceivedCallback) (char* message);

@interface MyUiDelegate : NSObject <WKUIDelegate, WKScriptMessageHandler> {
    @public
    NSWindow * window;
    WebMessageReceivedCallback webMessageReceivedCallback;
}
@end
