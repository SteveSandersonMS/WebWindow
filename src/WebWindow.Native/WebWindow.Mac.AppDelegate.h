#import <Cocoa/Cocoa.h>
#import <UserNotifications/UserNotifications.h>

@interface MyApplicationDelegate : NSObject <NSApplicationDelegate, NSWindowDelegate, UNUserNotificationCenterDelegate>
{
    NSWindow *window;
}
@end
