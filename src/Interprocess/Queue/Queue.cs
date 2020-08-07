﻿using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Cloudtoid.Interprocess
{
    internal abstract class Queue : IDisposable
    {
        private readonly InterprocessSemaphore receiversSignal;
        private readonly MemoryView view;
        protected readonly CircularBuffer buffer;

        protected unsafe Queue(QueueOptions options)
        {
            if (options is null)
                throw new ArgumentNullException(nameof(options));

            try
            {
                var path = Util.GetAbsolutePath(options.Path);
                var identifier = new SharedAssetsIdentifier(options.QueueName, path);
                receiversSignal = new InterprocessSemaphore(identifier);
                view = new MemoryView(options);
                buffer = new CircularBuffer(sizeof(QueueHeader) + view.Pointer, options.Capacity);
            }
            catch
            {
                view?.Dispose();
                receiversSignal?.Dispose();
                throw;
            }
        }

        public unsafe QueueHeader* Header
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (QueueHeader*)view.Pointer;
        }

        public virtual void Dispose()
        {
            view.Dispose();
            receiversSignal.Dispose();
        }

        /// <summary>
        /// Signals at most one receiver to attempt to see if there are any messages left in the queue.
        /// There are no guarantees that there are any messages left in the queue.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected Task SignalReceiversAsync(CancellationToken cancellation)
            => receiversSignal.ReleaseAsync(cancellation);

        /// <summary>
        /// Waits the maximum of <paramref name="millisecondsTimeout"/> for a signal that there might be
        /// more messages in the queue ready to be processed.
        /// NOTE: There are no guarantees that there are any messages left in the queue.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void WaitForReceiverSignal(int millisecondsTimeout)
            => receiversSignal.WaitOne(millisecondsTimeout);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected unsafe long GetMessageBodyOffset(long messageHeaderOffset)
            => sizeof(MessageHeader) + messageHeaderOffset;

        /// <summary>
        /// Calculates the total length of a message which consists of [header][body][padding].
        /// <list type="bullet">
        /// <item><term>header</term><description>An instance of <see cref="MessageHeader"/></description></item>
        /// <item><term>body</term><description>A collection of bytes provided by the user</description></item>
        /// <item><term>padding</term><description>A possible padding is added to round up the length to the closest multiple of 8 bytes</description></item>
        /// </list>
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected unsafe long GetMessageLength(long bodyLength)
        {
            var length = sizeof(MessageHeader) + bodyLength;

            // Round up to the closest integer divisible by 8. This will add the [padding] if one is needed.
            return 8 * (long)Math.Ceiling(length / 8.0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected static long SafeIncrementMessageOffset(long offset, long increment)
        {
            if (increment > long.MaxValue - offset)
                return -long.MaxValue + offset + increment; // Do NOT change the order of additions here

            return offset + increment;
        }
    }
}
