var app = new Vue({
    el: '#app',
    data: {
        directoryInfo: null,
        fileInfo: null
    },
    methods: {
        navigateTo: function (relativePath, event) {
            event.preventDefault();
            window.external.sendMessage(JSON.stringify({
                command: 'navigateTo',
                basePath: app.directoryInfo.name,
                relativePath: relativePath
            }));
        },
        showFile: function (fullName, event) {
            event.preventDefault();
            window.external.sendMessage(JSON.stringify({
                command: 'showFile',
                fullName: fullName
            }));
        }
    }
});

window.external.receiveMessage(function (messageJson) {
    var message = JSON.parse(messageJson);
    switch (message.command) {
        case 'showDirectory':
            app.directoryInfo = message.arg;
            break;
        case 'showFile':
            app.fileInfo = message.arg;
            break;
    }
});

window.external.sendMessage(JSON.stringify({ command: 'ready' }));
