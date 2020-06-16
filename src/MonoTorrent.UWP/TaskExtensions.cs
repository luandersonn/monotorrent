using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI.Core;

namespace MonoTorrent.UWP
{
    public static class TaskExtensions
    {
        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        public static async Task TimeoutAfter(this Task task, int millisecondsTimeout)
        {
            await TimeoutAfter(task, millisecondsTimeout, CancellationToken.None);
        }

        public static async Task TimeoutAfter(this Task task, int millisecondsTimeout, CancellationToken cancellationToken)
        {
            if (!Debugger.IsAttached)
            {
                if (task == await Task.WhenAny(task, Task.Delay(millisecondsTimeout, cancellationToken)))
                    await task;
                else
                    throw new TimeoutException();
            }
            else
            {
                await task;
            }
        }

        public static async Task<TResult> TimeoutAfter<TResult>(this Task<TResult> task, int millisecondsTimeout)
        {
            return await TimeoutAfter<TResult>(task, millisecondsTimeout, CancellationToken.None);
        }

        public static async Task<TResult> TimeoutAfter<TResult>(this Task<TResult> task, int millisecondsTimeout, CancellationToken cancellationToken)
        {
            if (!Debugger.IsAttached)
            {
                if (task == await Task.WhenAny(task, Task.Delay(millisecondsTimeout, cancellationToken)))
                    return await task;
                else
                    throw new TimeoutException();
            }
            else
            {
                return await task;
            }
        }

        private static async Task CancellableDelay(int millisecondsTimeout, int checkPeriodMilliseconds, CancellationToken cancellationToken)
        {
            int millisecondsLeft = millisecondsTimeout;
            do
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                await Task.Delay(checkPeriodMilliseconds);
                millisecondsLeft -= checkPeriodMilliseconds;
            } while (millisecondsLeft > 0);

        }

        public static async Task<TResult> TimeoutAfter<TResult>(this Task<TResult> task, int millisecondsTimeout, int checkPeriodMilliseconds, CancellationToken cancellationToken)
        {
            if (!Debugger.IsAttached)
            {
                if (task == await Task.WhenAny(task, CancellableDelay(millisecondsTimeout, checkPeriodMilliseconds, cancellationToken)))
                    return await task;
                else
                    throw new TimeoutException();
            }
            else
            {
                return await task;
            }
        }

        public static Task RunSafe(Action action)
        {
            return System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    logger.Info(ex, "Task.RunSafe exception");
                }
            });
        }

        public static IAsyncAction RunSafeAsync(this CoreDispatcher dispatcher, DispatchedHandler agileCallback)
        {
            return dispatcher.RunSafeAsync(CoreDispatcherPriority.Normal, agileCallback);
        }

        public static IAsyncAction RunSafeAsync(this CoreDispatcher dispatcher, CoreDispatcherPriority priority, DispatchedHandler agileCallback)
        {
            try
            {
                return dispatcher.RunAsync(priority, agileCallback);
            }
            catch (Exception ex)
            {
                logger.Info(ex, "Task.RunSafe(dispatcher) exception");
            }

            return null;
        }
    }


    public static class AsyncHelpers
    {
        /// <summary>
        /// Execute's an async Task<T> method which has a void return value synchronously
        /// </summary>
        /// <param name="task">Task<T> method to execute</param>
        public static void RunSync(Func<Task> task)
        {
            var oldContext = SynchronizationContext.Current;
            var synch = new ExclusiveSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(synch);
            synch.Post(async _ =>
            {
                try
                {
                    await task();
                }
                catch (Exception e)
                {
                    synch.InnerException = e;
                    throw;
                }
                finally
                {
                    synch.EndMessageLoop();
                }
            }, null);
            synch.BeginMessageLoop();

            SynchronizationContext.SetSynchronizationContext(oldContext);
        }

        /// <summary>
        /// Execute's an async Task<T> method which has a T return type synchronously
        /// </summary>
        /// <typeparam name="T">Return Type</typeparam>
        /// <param name="task">Task<T> method to execute</param>
        /// <returns></returns>
        public static T RunSync<T>(Func<Task<T>> task)
        {
            var oldContext = SynchronizationContext.Current;
            var synch = new ExclusiveSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(synch);
            T ret = default(T);
            synch.Post(async _ =>
            {
                try
                {
                    ret = await task();
                }
                catch (Exception e)
                {
                    synch.InnerException = e;
                    throw;
                }
                finally
                {
                    synch.EndMessageLoop();
                }
            }, null);
            synch.BeginMessageLoop();
            SynchronizationContext.SetSynchronizationContext(oldContext);
            return ret;
        }

        private class ExclusiveSynchronizationContext : SynchronizationContext
        {
            private bool done;
            public Exception InnerException { get; set; }
            readonly AutoResetEvent workItemsWaiting = new AutoResetEvent(false);
            readonly Queue<Tuple<SendOrPostCallback, object>> items =
                new Queue<Tuple<SendOrPostCallback, object>>();

            public override void Send(SendOrPostCallback d, object state)
            {
                throw new NotSupportedException("We cannot send to our same thread");
            }

            public override void Post(SendOrPostCallback d, object state)
            {
                lock (items)
                {
                    items.Enqueue(Tuple.Create(d, state));
                }
                workItemsWaiting.Set();
            }

            public void EndMessageLoop()
            {
                Post(_ => done = true, null);
            }

            public void BeginMessageLoop()
            {
                while (!done)
                {
                    Tuple<SendOrPostCallback, object> task = null;
                    lock (items)
                    {
                        if (items.Count > 0)
                        {
                            task = items.Dequeue();
                        }
                    }
                    if (task != null)
                    {
                        task.Item1(task.Item2);
                        if (InnerException != null) // the method threw an exeption
                        {
                            throw new AggregateException("AsyncHelpers.Run method threw an exception.", InnerException);
                        }
                    }
                    else
                    {
                        workItemsWaiting.WaitOne();
                    }
                }
            }

            public override SynchronizationContext CreateCopy()
            {
                return this;
            }
        }
    }
}
