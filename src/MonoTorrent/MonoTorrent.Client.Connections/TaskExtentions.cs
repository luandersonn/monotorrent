using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MonoTorrent.Client.Connections
{
    static class TaskExtentions
    {
        private static bool PreventTimeoutWhenDebuggerIsAttached = false;

        public static async Task TimeoutAfter(this Task task, int millisecondsTimeout)
        {
            await TimeoutAfter(task, millisecondsTimeout, CancellationToken.None);
        }

        public static async Task<bool> TimeoutAfterWithoutExeption(this Task task, int millisecondsTimeout)
        {
            try
            {
                await TimeoutAfter(task, millisecondsTimeout, CancellationToken.None);
            }
            catch(TimeoutException)
            {
                return true;
            }
            return false;
        }

        public static async Task TimeoutAfter(this Task task, int millisecondsTimeout, CancellationToken cancellationToken)
        {
            if (!PreventTimeoutWhenDebuggerIsAttached || !Debugger.IsAttached)
            {
                if (task == await Task.WhenAny(task, Task.Delay(millisecondsTimeout, cancellationToken)))
                {
                    await task;
                }
                else
                {
                   throw new TimeoutException();
                }
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
            if (!PreventTimeoutWhenDebuggerIsAttached || !Debugger.IsAttached)
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
    }
}
