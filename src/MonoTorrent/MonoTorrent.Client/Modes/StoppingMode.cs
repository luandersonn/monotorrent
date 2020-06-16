//
// StoppingMode.cs
//
// Authors:
//   Alan McGovern <alan.mcgovern@gmail.com>
//
// Copyright (C) 2008 Alan McGovern
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//


using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MonoTorrent.Client.Tracker;

namespace MonoTorrent.Client.Modes
{
    class StoppingMode : Mode
    {
        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger ();
        public override bool CanAcceptConnections => false;
        public override bool CanHandleMessages => false;
        public override bool CanHashCheck => false;
        public override TorrentState State => TorrentState.Stopping;

        public StoppingMode (TorrentManager manager, DiskManager diskManager, ConnectionManager connectionManager, EngineSettings settings)
            : base (manager, diskManager, connectionManager, settings)
        {
        }

        public Task WaitForStoppingToComplete ()
        {
            return WaitForStoppingToComplete (Timeout.InfiniteTimeSpan);
        }

        public async Task WaitForStoppingToComplete (TimeSpan timeout)
        {
            try {
                Manager.Engine.ConnectionManager.CancelPendingConnects (Manager);
                foreach (PeerId id in Manager.Peers.ConnectedPeers.ToArray ().AsParallel())
                    Manager.Engine.ConnectionManager.CleanupSocket (Manager, id);

                // Trying to make stopping faster.
                //var cleanupSocketsTask = Manager.Peers.ConnectedPeers.ToArray().Select(id => Manager.Engine.ConnectionManager.CleanupSocket(Manager, id));
                //await Task.WhenAll(cleanupSocketsTask);

                Manager.Monitor.Reset ();
                Manager.Peers.ClearAll ();
                Manager.PieceManager.Reset ();
                Manager.finishedPieces.Clear ();

                var stoppingTasks = new List<Task> {
                    Manager.Engine.DiskManager.CloseFilesAsync (Manager.Torrent)
                };

                Task announceTask = Manager.TrackerManager.Announce (TorrentEvent.Stopped);
                // Do not wait for announce to complete, it might take a while for torrents that have a lot of trackers.
                //if (timeout != Timeout.InfiniteTimeSpan)
                //    announceTask = Task.WhenAny (announceTask, Task.Delay (timeout));
                //stoppingTasks.Add (announceTask);

                var delayTask = Task.Delay (TimeSpan.FromSeconds (30), Cancellation.Token);
                var overallTasks = Task.WhenAll (stoppingTasks);
                if (await Task.WhenAny (overallTasks, delayTask) == delayTask)
                    logger.Info ("Timed out waiting for the announce request to complete");
            } catch (Exception ex) {
                logger.Error (ex, $"Unexpected exception stopping a TorrentManager: {Manager.Name}");
            }
        }
    }
}
