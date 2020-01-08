using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WebWindows;

namespace WebWindows.Blazor
{
    internal class IPC
    {
        private readonly Dictionary<string, List<Action<object>>> _registrations = new Dictionary<string, List<Action<object>>>();
        private readonly WebWindow _webWindow;

        public IPC(WebWindow webWindow)
        {
            _webWindow = webWindow ?? throw new ArgumentNullException(nameof(webWindow));
            _webWindow.OnWebMessageReceived += HandleScriptNotify;
        }

        public void Send(string eventName, params object[] args)
        {
            try
            {
                _webWindow.Invoke(() =>
                {
                    _webWindow.SendMessage($"{eventName}:{JsonSerializer.Serialize(args)}");
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        public void On(string eventName, Action<object> callback)
        {
            lock (_registrations)
            {
                if (!_registrations.TryGetValue(eventName, out var group))
                {
                    group = new List<Action<object>>();
                    _registrations.Add(eventName, group);
                }

                group.Add(callback);
            }
        }

        public void Once(string eventName, Action<object> callback)
        {
            Action<object> callbackOnce = null;
            callbackOnce = arg =>
            {
                Off(eventName, callbackOnce);
                callback(arg);
            };

            On(eventName, callbackOnce);
        }

        public void Off(string eventName, Action<object> callback)
        {
            lock (_registrations)
            {
                if (_registrations.TryGetValue(eventName, out var group))
                {
                    group.Remove(callback);
                }
            }
        }

        private void HandleScriptNotify(object sender, string message)
        {
            var value = message;

            // Move off the browser UI thread
            Task.Factory.StartNew(() =>
            {
                if (value.StartsWith("ipc:"))
                {
                    var spacePos = value.IndexOf(' ');
                    var eventName = value.Substring(4, spacePos - 4);
                    var argsJson = value.Substring(spacePos + 1);
                    var args = JsonSerializer.Deserialize<object[]>(argsJson);

                    Action<object>[] callbacksCopy;
                    lock (_registrations)
                    {
                        if (!_registrations.TryGetValue(eventName, out var callbacks))
                        {
                            return;
                        }

                        callbacksCopy = callbacks.ToArray();
                    }

                    foreach (var callback in callbacksCopy)
                    {
                        callback(args);
                    }
                }
            });
        }
    }
}
