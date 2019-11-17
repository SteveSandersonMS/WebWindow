interface Callback {
    (...args: any[]): void;
}

const registrations = {} as { [eventName: string]: Callback[] };

export function on(eventName: string, callback: Callback): void {
    if (!(eventName in registrations)) {
        registrations[eventName] = [];
    }

    registrations[eventName].push(callback);
}

export function off(eventName: string, callback: Callback): void {
    const group = registrations[eventName];
    const index = group.indexOf(callback);
    if (index >= 0) {
        group.splice(index, 1);
    }
}

export function once(eventName: string, callback: Callback): void {
    const callbackOnce: Callback = (...args: any[]) => {
        off(eventName, callbackOnce);
        callback.apply(null, args);
    };

    on(eventName, callbackOnce);
}

export function send(eventName: string, args: any): void {
    (window as any).external.sendMessage(`ipc:${eventName} ${JSON.stringify(args)}`);
}

(window as any).external.receiveMessage((message: string) => {
    const colonPos = message.indexOf(':');
    const eventName = message.substring(0, colonPos);
    const argsJson = message.substr(colonPos + 1);

    const group = registrations[eventName];
    if (group) {
        const args: any[] = JSON.parse(argsJson);
        group.forEach(callback => callback.apply(null, args));
    }
});
