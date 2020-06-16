//
// ConnectionManager.cs
//
// Authors:
//   Alan McGovern alan.mcgovern@gmail.com
//
// Copyright (C) 2006 Alan McGovern
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
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using MonoTorrent.BEncoding;
using MonoTorrent.Client.Connections;
using MonoTorrent.Client.Encryption;
using MonoTorrent.Client.Messages.Standard;
using MonoTorrent.Client.RateLimiters;
using NLog;
using ReusableTasks;

namespace MonoTorrent.Client
{
    /// <summary>
    /// Main controller class for all incoming and outgoing connections
    /// </summary>
    public class ConnectionManager
    {
        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger ();

        struct AsyncConnectState
        {
            public AsyncConnectState (TorrentManager manager, IConnection connection, Peer peer, ValueStopwatch timer)
            {
                Manager = manager;
                Peer = peer;
                Connection = connection;
                Timer = timer;
            }

            public readonly IConnection Connection;
            public readonly TorrentManager Manager;
            public Peer Peer;
            public readonly ValueStopwatch Timer;
        }

        public event EventHandler<AttemptConnectionEventArgs> BanPeer;

        internal static readonly int ChunkLength = 2096 + 64;   // Download in 2kB chunks to allow for better rate limiting

        internal DiskManager DiskManager { get; }

        internal BEncodedString LocalPeerId { get; }

        /// <summary>
        /// The number of concurrent connection attempts
        /// </summary>
        public int HalfOpenConnections => PendingConnects.Count;

        /// <summary>
        /// The maximum number of concurrent connection attempts
        /// </summary>
        internal int MaxHalfOpenConnections => Settings.MaximumHalfOpenConnections;

        /// <summary>
        /// The maximum number of open connections
        /// </summary>
        internal int MaxOpenConnections => Settings.MaximumConnections;

        /// <summary>
        /// The number of open connections
        /// </summary>
        public int OpenConnections => (int) ClientEngine.MainLoop.QueueWait (() =>
                     (int) Toolbox.Accumulate (Torrents, (m) =>
                         m.Peers.ConnectedPeers.Count
                     )
                );

        List<AsyncConnectState> PendingConnects { get; }

        EngineSettings Settings { get; }

        LinkedList<TorrentManager> Torrents { get; set; }

        internal ConnectionManager (BEncodedString localPeerId, EngineSettings settings, DiskManager diskManager)
        {
            DiskManager = diskManager ?? throw new ArgumentNullException (nameof (diskManager));
            LocalPeerId = localPeerId ?? throw new ArgumentNullException (nameof (localPeerId));
            Settings = settings ?? throw new ArgumentNullException (nameof (settings));

            PendingConnects = new List<AsyncConnectState> ();
            Torrents = new LinkedList<TorrentManager> ();
        }

        internal void Add (TorrentManager manager)
        {
            Torrents.AddLast (manager);
        }

        internal void Remove (TorrentManager manager)
            => Torrents.Remove (manager);

        async Task ConnectToPeer (TorrentManager manager, Peer peer)
        {
            // alekseyv hack
            //if (peer.ConnectionUri.Port != 38706 && peer.ConnectionUri.Port != 13037) {
            //    return;
            //}

            //hack
            if (manager.Peers.ActivePeers.Contains (peer)) {
                return;
            }

            // Connect to the peer.
            IConnection2 connection = ConnectionConverter.Convert (ConnectionFactory.Create (peer.ConnectionUri, Settings.EnabledProtocolType));
            if (connection == null)
                return;

            await ConnectToPeer (manager, peer, connection);
        }

        async Task ConnectToPeer (TorrentManager manager, Peer peer, IConnection2 connection)
        {
            logger.Info ("connecting to url: {0} using connection type: {1}", connection.Uri.ToString(), connection.GetType().Name);
            var state = new AsyncConnectState (manager, connection, peer, ValueStopwatch.StartNew ());
            PendingConnects.Add (state);
            manager.Peers.ConnectingToPeers.Add (peer);

            bool succeeded;
            try {
                await NetworkIO.ConnectAsync (connection);
                succeeded = true;
            } catch {
                succeeded = false;
            }

            PendingConnects.Remove (state);
            manager.Peers.ConnectingToPeers.Remove (peer);
            if (manager.Engine == null || !manager.Mode.CanAcceptConnections) {
                manager.Peers.AvailablePeers.Add (peer);
                connection.Dispose ();
                return;
            }

            try {
                if (!succeeded) {
                    peer.FailedConnectionAttempts++;
                    connection.Dispose ();
                    manager.RaiseConnectionAttemptFailed (new ConnectionAttemptFailedEventArgs (peer, ConnectionFailureReason.Unreachable, manager));
                } else {
                    PeerId id = new PeerId (peer, connection, manager.Bitfield?.Clone ().SetAll (false));
                    id.LastMessageReceived.Restart ();
                    id.LastMessageSent.Restart ();

                    logger.Info (id.Connection, "ConnectionManager - Connection opened");
                    logger.Info ($"connected to peer: {id.ToDebugString ()}");

                    succeeded = await ProcessNewOutgoingConnection (manager, id);

                    if (!succeeded) {
                        connection = ConnectionFactory.GetFallbackProtocolConnection (peer.ConnectionUri, id.ProtocolType, Settings.EnabledProtocolType);
                        if (connection == null)
                            return;

                        peer.AllowedEncryption = EncryptionTypes.All;
                        logger.Info ($"reconnecting using fallback protocol type: {id.ToDebugString ()}");
                        await ConnectToPeer (manager, peer, connection);
                    }
               }
            } catch {
                // FIXME: Do nothing now?
            } finally {
                // Try to connect to another peer
                await TryConnect ();
            }
        }

        internal bool Contains (TorrentManager manager)
        {
            return Torrents.Contains (manager);
        }

        internal async Task<bool> ProcessNewOutgoingConnection (TorrentManager manager, PeerId id)
        {
            // If we have too many open connections, close the connection
            if (OpenConnections > MaxOpenConnections) {
                CleanupSocket (manager, id);
                return false;
            }

            // TODO: alekseyv - put logic utp switching logic here

            id.ProcessingQueue = true;
            manager.Peers.ActivePeers.Add (id.Peer);
            manager.Peers.ConnectedPeers.Add (id);

            try {
                // Create a handshake message to send to the peer
                var handshake = new HandshakeMessage (manager.InfoHash, LocalPeerId, VersionInfo.ProtocolStringV100);
                EncryptorFactory.EncryptorResult result = await EncryptorFactory.CheckOutgoingConnectionAsync (id.Connection, id.Peer.AllowedEncryption, Settings, manager.InfoHash, handshake);
                id.Decryptor = result.Decryptor;
                id.Encryptor = result.Encryptor;
            }
            catch (Exception ex)
            {
                logger.Info($"Exception !!! {id.ToDebugString ()} sending handshake failed)");
                // If an exception is thrown it's because we tried to establish an encrypted connection and something went wrong
                id.Peer.AllowedEncryption &= ~(EncryptionTypes.RC4Full | EncryptionTypes.RC4Header);

                manager.RaiseConnectionAttemptFailed (new ConnectionAttemptFailedEventArgs (id.Peer, ConnectionFailureReason.EncryptionNegiotiationFailed, manager));
                CleanupSocket (manager, id);

                // CleanupSocket will contain the peer only if AllowedEncryption is not set to None. If
                // the peer was re-added, then we should try to reconnect to it immediately to try an
                // unencrypted connection.
                if (manager.Peers.AvailablePeers.Remove (id.Peer))
                    await ConnectToPeer (manager, id.Peer);
                return false;
            }

            try {
                // Receive their handshake
                HandshakeMessage handshake = await PeerIO.ReceiveHandshakeAsync (id.Connection, id.Decryptor);
                handshake.Handle(manager, id);
                logger.Info($"error !!!! {id.ToDebugString()} handled handshake !!!!!!!!!!!");
            } catch (Exception ex) {
                logger.Info($"Exception !!! {id.ToDebugString()} ReceiveHandshakeAsync handshake failed");
                // If we choose plaintext and it resulted in the connection being closed, remove it from the list.
                id.Peer.AllowedEncryption &= ~id.EncryptionType;

                manager.RaiseConnectionAttemptFailed (new ConnectionAttemptFailedEventArgs (id.Peer, ConnectionFailureReason.HandshakeFailed, manager));
                CleanupSocket (manager, id);

                // TODO: alekseyv - use similar logic in order to try reconnecto to autoselected protocol. Move selection logic into peer instead of connection.

                // CleanupSocket will contain the peer only if AllowedEncryption is not set to None. If
                // the peer was re-added, then we should try to reconnect to it immediately to try an
                // encrypted connection, assuming the previous connection was unencrypted and it failed.
                if (manager.Peers.AvailablePeers.Remove (id.Peer))
                    await ConnectToPeer (manager, id.Peer);

                return false;
            }

            try {
                if (id.BitField.Length != manager.Bitfield.Length)
                    throw new TorrentException ($"The peer's bitfield was of length {id.BitField.Length} but the TorrentManager's bitfield was of length {manager.Bitfield.Length}.");
                manager.HandlePeerConnected (id);

                // If there are any pending messages, send them otherwise set the queue
                // processing as finished.
                if (id.QueueLength > 0)
                    ProcessQueue (manager, id);
                else
                    id.ProcessingQueue = false;

                await ReceiveMessagesAsync (id.Connection, id.Decryptor, manager.DownloadLimiters, id.Monitor, manager, id);

                id.WhenConnected.Restart ();
                id.LastBlockReceived.Restart ();
            } catch {
                manager.RaiseConnectionAttemptFailed (new ConnectionAttemptFailedEventArgs (id.Peer, ConnectionFailureReason.Unknown, manager));
                CleanupSocket (manager, id);
                return false;
            }

            logger.Info ($"error !!!! {id.ToDebugString ()} peer processed !!!!!!!!!!!");
            return true;
        }

        internal async Task ReceiveMessagesAsync (IConnection2 connection, IEncryption decryptor, RateLimiterGroup downloadLimiter, ConnectionMonitor monitor, TorrentManager torrentManager, PeerId id)
        {
            try {
                while (true) {
                    Messages.PeerMessage message = await PeerIO.ReceiveMessageAsync (connection, decryptor, downloadLimiter, monitor, torrentManager.Monitor, torrentManager.Torrent);
                    logger.Info("Received message:" + message != null ? message.ToString() : "null");
                    if (id.Disposed) {
                        if (message is PieceMessage msg)
                            ClientEngine.BufferPool.Return (msg.Data);
                    } else {
                        id.LastMessageReceived.Restart ();
                        message.Handle (torrentManager, id);
                    }
                }
            } catch {
                CleanupSocket (torrentManager, id);
            }
        }

        internal void CleanupSocket (TorrentManager manager, PeerId id)
        {
            if (id == null || id.Disposed) // Sometimes onEncryptoError will fire with a null id
                return;

            try {
                // We can reuse this peer if the connection says so and it's not marked as inactive
                bool canReuse = (id.Connection?.CanReconnect ?? false)
                    && !manager.InactivePeerManager.InactivePeerList.Contains (id.Uri)
                    && id.Peer.AllowedEncryption != EncryptionTypes.None;

                manager.PieceManager.Picker.CancelRequests (id);
                id.Peer.CleanedUpCount++;

                id.PeerExchangeManager?.Dispose ();

                if (!id.AmChoking)
                    manager.UploadingTo--;

                manager.Peers.ConnectedPeers.Remove (id);
                manager.Peers.ActivePeers.Remove (id.Peer);

                // If we get our own details, this check makes sure we don't try connecting to ourselves again
                if (canReuse && !LocalPeerId.Equals (id.Peer.PeerId)) {
                    if (!manager.Peers.AvailablePeers.Contains (id.Peer) && id.Peer.CleanedUpCount < 5)
                        manager.Peers.AvailablePeers.Insert (0, id.Peer);
                    else if (manager.Peers.BannedPeers.Contains (id.Peer) && id.Peer.CleanedUpCount >= 5)
                        manager.Peers.BannedPeers.Add (id.Peer);
                }
            } catch (Exception ex) {
                logger.Info (id.Connection, $"CleanupSocket Error {ex.Message}");
            } finally {
                manager.RaisePeerDisconnected (new PeerDisconnectedEventArgs (manager, id));
            }

            id.Dispose ();
        }

        /// <summary>
        /// Cancel all pending connection for the given <see cref="TorrentManager"/>, or which have exceeded <see cref="EngineSettings.ConnectionTimeout"/>
        /// </summary>
        internal void CancelPendingConnects (TorrentManager manager)
        {
            Func<AsyncConnectState, bool> connectionsToCancelPredicate = (AsyncConnectState pending) => (pending.Manager == manager && pending.Timer.Elapsed > Settings.ConnectionTimeout);
            List<AsyncConnectState> connectionsToCancel = PendingConnects.Where(connectionsToCancelPredicate).ToList();
            HashSet<Peer> peersWithConnectionsToCancel = new HashSet<Peer>(connectionsToCancel.Select (state => state.Peer));
            PendingConnects.RemoveAll(pending => connectionsToCancelPredicate(pending));
            manager.Peers.ConnectingToPeers.RemoveAll(peer => peersWithConnectionsToCancel.Contains(peer));
            connectionsToCancel.ForEach(pending => pending.Connection.Dispose());

            // Original MonoTorrent code:
            //foreach (var pending in PendingConnects)
            //{
            //    if (manager == null || (pending.Manager == manager && pending.Timer.Elapsed > Settings.ConnectionTimeout))
            //    {
            //        pending.Connection.Dispose();
            //    }
            //}
        }

        /// <summary>
        /// This method is called when the ClientEngine recieves a valid incoming connection
        /// </summary>
        /// <param name="manager">The torrent which the peer is associated with.</param>
        /// <param name="id">The peer who just conencted</param>
        internal async ReusableTask<bool> IncomingConnectionAcceptedAsync (TorrentManager manager, PeerId id)
        {
            try {
                bool maxAlreadyOpen = OpenConnections >= Math.Min (MaxOpenConnections, manager.Settings.MaximumConnections);
                if (LocalPeerId.Equals (id.Peer.PeerId) || maxAlreadyOpen) {
                    CleanupSocket (manager, id);
                    return false;
                }

                if (manager.Peers.ActivePeers.Contains (id.Peer)) {
                    logger.Info (id.Connection, "ConnectionManager - Already connected to peer");
                    id.Connection.Dispose ();
                    return false;
                }

                // Add the PeerId to the lists *before* doing anything asynchronous. This ensures that
                // all PeerIds are tracked in 'ConnectedPeers' as soon as they're created.
                logger.Info (id.Connection, "ConnectionManager - Incoming connection fully accepted");
                manager.Peers.AvailablePeers.Remove (id.Peer);
                manager.Peers.ActivePeers.Add (id.Peer);
                manager.Peers.ConnectedPeers.Add (id);

                id.WhenConnected.Restart ();
                // Baseline the time the last block was received
                id.LastBlockReceived.Restart ();

                // Send our handshake now that we've decided to keep the connection
                var handshake = new HandshakeMessage (manager.InfoHash, manager.Engine.PeerId, VersionInfo.ProtocolStringV100);
                await PeerIO.SendMessageAsync (id.Connection, id.Encryptor, handshake, manager.UploadLimiters, id.Monitor, manager.Monitor);

                manager.HandlePeerConnected (id);

                // We've sent our handshake so begin our looping to receive incoming message
                ReceiveMessagesAsync (id.Connection, id.Decryptor, manager.DownloadLimiters, id.Monitor, manager, id);
                return true;
            } catch {
                CleanupSocket (manager, id);
                return false;
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="manager">The torrent which the peer is associated with.</param>
        /// <param name="id">The peer whose message queue you want to start processing</param>
        internal async void ProcessQueue (TorrentManager manager, PeerId id)
        {
            while (id.QueueLength > 0) {
                Messages.PeerMessage msg = id.Dequeue ();
                var pm = msg as PieceMessage;

                try {
                    if (pm != null) {
                        pm.Data = ClientEngine.BufferPool.Rent (pm.ByteLength);
                        try {
                            await DiskManager.ReadAsync (manager.Torrent, pm.StartOffset + ((long) pm.PieceIndex * manager.Torrent.PieceLength), pm.Data, pm.RequestLength);
                        } catch (Exception ex) {
                            manager.TrySetError (Reason.ReadFailure, ex);
                            return;
                        }
                        id.PiecesSent++;
                    }

                    await PeerIO.SendMessageAsync (id.Connection, id.Encryptor, msg, manager.UploadLimiters, id.Monitor, manager.Monitor);
                    if (msg is PieceMessage)
                        id.IsRequestingPiecesCount--;

                    id.LastMessageSent.Restart ();
                } catch {
                    CleanupSocket (manager, id);
                    break;
                } finally {
                    if (pm?.Data != null)
                        ClientEngine.BufferPool.Return (pm.Data);
                }
            }

            id.ProcessingQueue = false;
        }

        internal bool ShouldBanPeer (Peer peer)
        {
            if (BanPeer == null)
                return false;

            var e = new AttemptConnectionEventArgs (peer);
            BanPeer (this, e);
            return e.BanPeer;
        }

        internal async Task TryConnect ()
        {
            // If we have already reached our max connections globally, don't try to connect to a new peer
            while (OpenConnections <= MaxOpenConnections && PendingConnects.Count < MaxHalfOpenConnections) {
                LinkedListNode<TorrentManager> node = Torrents.First;
                while (node != null) {
                    // If we successfully connect, then break out of this loop and restart our
                    // connection process from the first node in the list again.
                    if (await TryConnect (node.Value)) {
                        Torrents.Remove (node);
                        Torrents.AddLast (node);
                        break;
                    }

                    // If we did not successfully connect to a peer, then try the next torrent.
                    node = node.Next;
                }

                // If we failed to connect to anyone after walking the entire list, give up for now.
                if (node == null)
                    break;
            }
        }

        async Task<bool> TryConnect (TorrentManager manager)
        {
            int i;
            Peer peer;
            if (!manager.Mode.CanAcceptConnections)
                return false;

            // If we have reached the max peers allowed for this torrent, don't connect to a new peer for this torrent
            if (manager.Peers.ConnectedPeers.Count >= manager.Settings.MaximumConnections)
                return false;

            // If the torrent isn't active, don't connect to a peer for it
            if (!manager.Mode.CanAcceptConnections)
                return false;

            // If we are not seeding, we can connect to anyone. If we are seeding, we should only connect to a peer
            // if they are not a seeder.
            for (i = 0; i < manager.Peers.AvailablePeers.Count; i++)
                if (manager.Mode.ShouldConnect (manager.Peers.AvailablePeers[i]))
                    break;

            // If this is true, there were no peers in the available list to connect to.
            if (i == manager.Peers.AvailablePeers.Count)
                return false;

            // Remove the peer from the lists so we can start connecting to him
            peer = manager.Peers.AvailablePeers[i];
            manager.Peers.AvailablePeers.RemoveAt (i);

            // Acitve peers should not have any peer from ConnectingToPeers list, doublechecking in any case.
            if (manager.Peers.ConnectingToPeers.Contains(peer)) 
                return false;

            if (ShouldBanPeer(peer))
                return false;

            // Connect to the peer
            await ConnectToPeer (manager, peer);
            return true;
        }
    }
}
