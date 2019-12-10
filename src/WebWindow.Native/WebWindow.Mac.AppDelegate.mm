#import "WebWindow.Mac.AppDelegate.h"
#include "WebWindow.h"
#include <map>

extern map<NSWindow*, WebWindow*> nsWindowToWebWindow;

@implementation MyApplicationDelegate : NSObject
- (id)init {
    if (self = [super init]) {
        // allocate and initialize window and stuff here ..
    }
    return self;
}

- (void)applicationDidFinishLaunching:(NSNotification *)notification {
    [window makeKeyAndOrderFront:nil];
    [NSApp activateIgnoringOtherApps:YES];
}

- (void)windowDidResize:(NSNotification *)notification {
    NSWindow *window = notification.object;
    WebWindow* webWindow = nsWindowToWebWindow[hwnd];
    if (webWindow)
    {
        int width, height;
        webWindow->GetSize(&width, &height);
        webWindow->InvokeResized(width, height);
    }
}

- (void)windowDidMove:(NSNotification *)notification {
    NSWindow *window = notification.object;
    WebWindow* webWindow = nsWindowToWebWindow[hwnd];
    if (webWindow)
    {
        int x, y;
        webWindow->GetPosition(&x, &y);
        webWindow->InvokeMoved(x, y);
    }
}

- (void)dealloc {
    [window release];
    [super dealloc];
}

@end
