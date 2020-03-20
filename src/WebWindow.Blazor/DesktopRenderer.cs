using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace WebWindows.Blazor
{
    // Many aspects of the layering here are not what we really want, but it won't affect
    // people prototyping applications with it. We can put more work into restructuring the
    // hosting and startup models in the future if it's justified.

    internal class DesktopRenderer : Renderer
    {
        private static readonly Task CanceledTask = Task.FromCanceled(new CancellationToken(canceled: true));

        private const int RendererId = 0; // Not relevant, since we have only one renderer in Desktop
        private readonly IPC _ipc;
        private readonly IJSRuntime _jsRuntime;
        private static readonly Type _writer;
        private static readonly MethodInfo _writeMethod;
        internal readonly ConcurrentQueue<UnacknowledgedRenderBatch> _unacknowledgedRenderBatches = new ConcurrentQueue<UnacknowledgedRenderBatch>();
        private bool _disposing = false;
        private long _nextRenderId = 1;

        public override Dispatcher Dispatcher { get; }

        static DesktopRenderer()
        {
            _writer = typeof(RenderBatchWriter);
            _writeMethod = _writer.GetMethod("Write", new[] { typeof(RenderBatch).MakeByRefType() });
        }

        public DesktopRenderer(IServiceProvider serviceProvider, IPC ipc, ILoggerFactory loggerFactory, IJSRuntime jSRuntime, Dispatcher dispatcher)
            : base(serviceProvider, loggerFactory)
        {
            _ipc = ipc ?? throw new ArgumentNullException(nameof(ipc));
            Dispatcher = dispatcher;
            _jsRuntime = jSRuntime;
        }

        /// <summary>
        /// Notifies when a rendering exception occured.
        /// </summary>
        public event EventHandler<Exception> UnhandledException;

        /// <summary>
        /// Attaches a new root component to the renderer,
        /// causing it to be displayed in the specified DOM element.
        /// </summary>
        /// <typeparam name="TComponent">The type of the component.</typeparam>
        /// <param name="domElementSelector">A CSS selector that uniquely identifies a DOM element.</param>
        public Task AddComponentAsync<TComponent>(string domElementSelector)
            where TComponent : IComponent
        {
            return AddComponentAsync(typeof(TComponent), domElementSelector);
        }

        /// <summary>
        /// Associates the <see cref="IComponent"/> with the <see cref="BrowserRenderer"/>,
        /// causing it to be displayed in the specified DOM element.
        /// </summary>
        /// <param name="componentType">The type of the component.</param>
        /// <param name="domElementSelector">A CSS selector that uniquely identifies a DOM element.</param>
        public Task AddComponentAsync(Type componentType, string domElementSelector)
        {
            var component = InstantiateComponent(componentType);
            var componentId = AssignRootComponentId(component);

            var attachComponentTask = _jsRuntime.InvokeAsync<object>(
                "Blazor._internal.attachRootComponentToElement",
                domElementSelector,
                componentId,
                RendererId);
            CaptureAsyncExceptions(attachComponentTask);
            return RenderRootComponentAsync(componentId);
        }

        /// <inheritdoc />
        protected override Task UpdateDisplayAsync(in RenderBatch batch)
        {
            if (_disposing)
            {
                // We are being disposed, so do no work.
                return CanceledTask;
            }

            string base64;
            using (var memoryStream = new MemoryStream())
            {
                object renderBatchWriter = Activator.CreateInstance(_writer, new object[] { memoryStream, false });
                using (renderBatchWriter as IDisposable)
                {
                    _writeMethod.Invoke(renderBatchWriter, new object[] { batch });
                }

                var batchBytes = memoryStream.ToArray();
                base64 = Convert.ToBase64String(batchBytes);
            }

            var renderId = Interlocked.Increment(ref _nextRenderId);

            var pendingRender = new UnacknowledgedRenderBatch(
                renderId,
                new TaskCompletionSource<object>());

            // Buffer the rendered batches no matter what. We'll send it down immediately when the client
            // is connected or right after the client reconnects.

            _unacknowledgedRenderBatches.Enqueue(pendingRender);

            _ipc.Send("JS.RenderBatch", renderId, base64);

            return pendingRender.CompletionSource.Task;
        }

        public Task OnRenderCompletedAsync(long incomingBatchId, string errorMessageOrNull)
        {
            if (_disposing)
            {
                // Disposing so don't do work.
                return Task.CompletedTask;
            }

            // When clients send acks we know for sure they received and applied the batch.
            // We send batches right away, and hold them in memory until we receive an ACK.
            // If one or more client ACKs get lost (e.g., with long polling, client->server delivery is not guaranteed)
            // we might receive an ack for a higher batch.
            // We confirm all previous batches at that point (because receiving an ack is guarantee
            // from the client that it has received and successfully applied all batches up to that point).

            // If receive an ack for a previously acknowledged batch, its an error, as the messages are
            // guaranteed to be delivered in order, so a message for a render batch of 2 will never arrive
            // after a message for a render batch for 3.
            // If that were to be the case, it would just be enough to relax the checks here and simply skip
            // the message.

            // A batch might get lost when we send it to the client, because the client might disconnect before receiving and processing it.
            // In this case, once it reconnects the server will re-send any unacknowledged batches, some of which the
            // client might have received and even believe it did send back an acknowledgement for. The client handles
            // those by re-acknowledging.

            // Even though we're not on the renderer sync context here, it's safe to assume ordered execution of the following
            // line (i.e., matching the order in which we received batch completion messages) based on the fact that SignalR
            // synchronizes calls to hub methods. That is, it won't issue more than one call to this method from the same hub
            // at the same time on different threads.

            if (!_unacknowledgedRenderBatches.TryPeek(out var nextUnacknowledgedBatch) || incomingBatchId < nextUnacknowledgedBatch.BatchId)
            {
                // TODO: Log duplicated batch ack.
                return Task.CompletedTask;
            }
            else
            {
                var lastBatchId = nextUnacknowledgedBatch.BatchId;
                // Order is important here so that we don't prematurely dequeue the last nextUnacknowledgedBatch
                while (_unacknowledgedRenderBatches.TryPeek(out nextUnacknowledgedBatch) && nextUnacknowledgedBatch.BatchId <= incomingBatchId)
                {
                    lastBatchId = nextUnacknowledgedBatch.BatchId;
                    // At this point the queue is definitely not full, we have at least emptied one slot, so we allow a further
                    // full queue log entry the next time it fills up.
                    _unacknowledgedRenderBatches.TryDequeue(out _);
                    ProcessPendingBatch(errorMessageOrNull, nextUnacknowledgedBatch);
                }

                if (lastBatchId < incomingBatchId)
                {
                    // This exception is due to a bad client input, so we mark it as such to prevent logging it as a warning and
                    // flooding the logs with warnings.
                    throw new InvalidOperationException($"Received an acknowledgement for batch with id '{incomingBatchId}' when the last batch produced was '{lastBatchId}'.");
                }

                // Normally we will not have pending renders, but it might happen that we reached the limit of
                // available buffered renders and new renders got queued.
                // Invoke ProcessBufferedRenderRequests so that we might produce any additional batch that is
                // missing.

                // We return the task in here, but the caller doesn't await it.
                return Dispatcher.InvokeAsync(() =>
                {
                    // Now we're on the sync context, check again whether we got disposed since this
                    // work item was queued. If so there's nothing to do.
                    if (!_disposing)
                    {
                        ProcessPendingRender();
                    }
                });
            }
        }

        private void ProcessPendingBatch(string errorMessageOrNull, UnacknowledgedRenderBatch entry)
        {
            CompleteRender(entry.CompletionSource, errorMessageOrNull);
        }

        private void CompleteRender(TaskCompletionSource<object> pendingRenderInfo, string errorMessageOrNull)
        {
            if (errorMessageOrNull == null)
            {
                pendingRenderInfo.TrySetResult(null);
            }
            else
            {
                pendingRenderInfo.TrySetException(new InvalidOperationException(errorMessageOrNull));
            }
        }

        private async void CaptureAsyncExceptions(ValueTask<object> task)
        {
            try
            {
                await task;
            }
            catch (Exception ex)
            {
                UnhandledException?.Invoke(this, ex);
            }
        }

        protected override void HandleException(Exception exception)
        {
            Console.WriteLine(exception.ToString());
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            _disposing = true;
            while (_unacknowledgedRenderBatches.TryDequeue(out var entry))
            {
                entry.CompletionSource.TrySetCanceled();
            }
            base.Dispose(true);
        }

        internal readonly struct UnacknowledgedRenderBatch
        {
            public UnacknowledgedRenderBatch(long batchId, TaskCompletionSource<object> completionSource)
            {
                BatchId = batchId;
                CompletionSource = completionSource;
            }

            public long BatchId { get; }

            public TaskCompletionSource<object> CompletionSource { get; }
        }
    }
}
