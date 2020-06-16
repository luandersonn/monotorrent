using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace MonoTorrent
{
    public static class Retry
    {
        public static void Do (
            Action action,
            TimeSpan retryInterval,
            int maxAttemptCount = 3,
            bool throwLast = false)
        {
            Do<object> (() =>
            {
                action ();
                return null;
            }, retryInterval, maxAttemptCount, throwLast);
        }

        public static T Do<T> (
            Func<T> action,
            TimeSpan retryInterval,
            int maxAttemptCount = 3,
            bool throwLast = false)
        {
            var exceptions = new List<Exception> ();

            for (int attempted = 0; attempted < maxAttemptCount; attempted++) {
                try {
                    if (attempted > 0) {
                        Thread.Sleep (retryInterval);
                    }
                    return action ();
                } catch (Exception ex) {
                    exceptions.Add (ex);
                }
            }
            if (throwLast) {
                throw exceptions.Last ();
            }
            throw new AggregateException (exceptions);
        }
    }
}
