using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace WebWindows.Blazor
{
    internal class DesktopSynchronizationContext : SynchronizationContext
    {
        public static event EventHandler<Exception> UnhandledException;

        private readonly WorkQueue _work;

        public DesktopSynchronizationContext(CancellationToken cancellationToken)
        {
            _work = new WorkQueue(cancellationToken);
        }

        public override SynchronizationContext CreateCopy()
        {
            return this;
        }

        public override void Post(SendOrPostCallback d, object state)
        {
            _work.Queue.Add(new WorkItem() { Callback = d, Context = this, State = state, });
        }

        public override void Send(SendOrPostCallback d, object state)
        {
            if (_work.CheckAccess())
            {
                _work.ProcessWorkitemInline(d, state);
            }
            else
            {
                var completed = new ManualResetEventSlim();
                _work.Queue.Add(new WorkItem() { Callback = d, Context = this, State = state, Completed = completed, });
                completed.Wait();
            }
        }

        public void Stop()
        {
            _work.Queue.CompleteAdding();
        }

        public static void CheckAccess()
        {
            var synchronizationContext = Current as DesktopSynchronizationContext;
            if (synchronizationContext == null)
            {
                throw new InvalidOperationException("Not in the right context.");
            }

            synchronizationContext._work.CheckAccess();
        }

        private class WorkQueue
        {
            private readonly Thread _thread;
            private readonly CancellationToken _cancellationToken;

            public WorkQueue(CancellationToken cancellationToken)
            {
                _cancellationToken = cancellationToken;
                _thread = new Thread(ProcessQueue);
                _thread.Start();
            }

            public BlockingCollection<WorkItem> Queue { get; } = new BlockingCollection<WorkItem>();

            public bool CheckAccess()
            {
                return Thread.CurrentThread == _thread;
            }

            private void ProcessQueue()
            {
                while (!Queue.IsCompleted)
                {
                    WorkItem item;
                    try
                    {
                        item = Queue.Take(_cancellationToken);
                    }
                    catch (InvalidOperationException)
                    {
                        return;
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    var current = Current;
                    SetSynchronizationContext(item.Context);

                    try
                    {
                        ProcessWorkitemInline(item.Callback, item.State);
                    }
                    finally
                    {
                        if (item.Completed != null)
                        {
                            item.Completed.Set();
                        }

                        SetSynchronizationContext(current);
                    }
                }
            }

            public void ProcessWorkitemInline(SendOrPostCallback callback, object state)
            {
                try
                {
                    callback(state);
                }
                catch (Exception e)
                {
                    UnhandledException?.Invoke(this, e);
                }
            }
        }

        private class WorkItem
        {
            public SendOrPostCallback Callback;
            public object State;
            public SynchronizationContext Context;
            public ManualResetEventSlim Completed;
        }
    }
}
