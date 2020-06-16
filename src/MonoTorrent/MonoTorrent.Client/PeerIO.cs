//
// PeerIO.cs
//
// Authors:
//   Alan McGovern alan.mcgovern@gmail.com
//
// Copyright (C) 2010 Alan McGovern
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
using System.Diagnostics;
using System.Net;

using MonoTorrent.Client.Connections;
using MonoTorrent.Client.Encryption;
using MonoTorrent.Client.Messages;
using MonoTorrent.Client.Messages.Standard;
using MonoTorrent.Client.RateLimiters;

using ReusableTasks;

namespace MonoTorrent.Client
{
    static class PeerIO
    {
        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger ();
        const int MaxMessageLength = Piece.BlockSize * 4;

        public static async ReusableTask<HandshakeMessage> ReceiveHandshakeAsync (IConnection2 connection, IEncryption decryptor)
        {
            byte[] buffer = ClientEngine.BufferPool.Rent (HandshakeMessage.HandshakeLength);
            try {
                logger.Info (connection, "Receiving HandshakeMessage");
                await NetworkIO.ReceiveAsync (connection, buffer, 0, HandshakeMessage.HandshakeLength, null, null, null).ConfigureAwait (false);

                decryptor.Decrypt (buffer, 0, HandshakeMessage.HandshakeLength);

                var message = new HandshakeMessage ();
                message.Decode (buffer, 0, HandshakeMessage.HandshakeLength);
                logger.Info (connection, "Received HandshakeMessage");
                return message;
            } finally {
                ClientEngine.BufferPool.Return (buffer);
            }
        }

        public static ReusableTask<PeerMessage> ReceiveMessageAsync (IConnection2 connection, IEncryption decryptor)
        {
            return ReceiveMessageAsync (connection, decryptor, null, null, null, null);
        }

        public static async ReusableTask<PeerMessage> ReceiveMessageAsync (IConnection2 connection, IEncryption decryptor, IRateLimiter rateLimiter, ConnectionMonitor peerMonitor, ConnectionMonitor managerMonitor, ITorrentData torrentData)
        {
            byte[] messageLengthBuffer = null;
            byte[] messageBuffer = null;

            int messageLength = 4;
            int messageBodyLength;
            try {
                messageLengthBuffer = ClientEngine.BufferPool.Rent (messageLength);
                logger.Info (connection, "Receiving message");

                await NetworkIO.ReceiveAsync (connection, messageLengthBuffer, 0, messageLength, rateLimiter, peerMonitor?.ProtocolDown, managerMonitor?.ProtocolDown).ConfigureAwait (false);

                decryptor.Decrypt (messageLengthBuffer, 0, messageLength);

                messageBodyLength = IPAddress.HostToNetworkOrder (BitConverter.ToInt32 (messageLengthBuffer, 0));
                logger.Info (connection, $"Received message excluding body, body length {messageBodyLength}");

                if (messageBodyLength < 0 || messageBodyLength > MaxMessageLength) {
                    connection.Dispose ();
                    throw new ProtocolException ($"Invalid message length received. Value was '{messageBodyLength}'");
                }

                if (messageBodyLength == 0)
                {
                    logger.Info (connection, $"Received message body length = 0, assuming it is KeepAliveMessage");
                    return new KeepAliveMessage ();
                }

                messageBuffer = ClientEngine.BufferPool.Rent (messageBodyLength + messageLength);
                Buffer.BlockCopy (messageLengthBuffer, 0, messageBuffer, 0, messageLength);
            } finally {
                ClientEngine.BufferPool.Return (messageLengthBuffer);
            }

            try {
                logger.Info (connection, "Receiving body");
                // Always assume protocol first, then convert to data when we what message it is!
                // note that offset is different here - reading the remainder
                await NetworkIO.ReceiveAsync (connection, messageBuffer, messageLength, messageBodyLength, rateLimiter, peerMonitor?.ProtocolDown, managerMonitor?.ProtocolDown).ConfigureAwait (false);

                decryptor.Decrypt (messageBuffer, messageLength, messageBodyLength);
                // FIXME: manager should never be null, except some of the unit tests do that.
                var data = PeerMessage.DecodeMessage (messageBuffer, 0, messageLength + messageBodyLength, torrentData);

                logger.Info (connection, $"Received message: {data}");

                if (data is PieceMessage msg) {
                    peerMonitor?.ProtocolDown.AddDelta (-msg.RequestLength);
                    managerMonitor?.ProtocolDown.AddDelta (-msg.RequestLength);

                    peerMonitor?.DataDown.AddDelta (msg.RequestLength);
                    managerMonitor?.DataDown.AddDelta (msg.RequestLength);
                }
                return data;
            } finally {
                ClientEngine.BufferPool.Return (messageBuffer);
            }
        }

        public static ReusableTask SendMessageAsync (IConnection2 connection, IEncryption encryptor, PeerMessage message)
        {
            return SendMessageAsync (connection, encryptor, message, null, null, null);
        }

        public static async ReusableTask SendMessageAsync (IConnection2 connection, IEncryption encryptor, PeerMessage message, IRateLimiter rateLimiter, ConnectionMonitor peerMonitor, ConnectionMonitor managerMonitor)
        {
            int count = message.ByteLength;
            byte[] buffer = ClientEngine.BufferPool.Rent (count);

            try {
                var pieceMessage = message as PieceMessage;
                message.Encode (buffer, 0);
                encryptor.Encrypt (buffer, 0, count);

                // Assume protocol first, then swap it to data once we successfully send the data bytes.
                logger.Info (connection, $"Sending message: {message}");

                await NetworkIO.SendAsync (connection, buffer, 0, count, pieceMessage == null ? null : rateLimiter, peerMonitor?.ProtocolUp, managerMonitor?.ProtocolUp).ConfigureAwait (false);
                logger.Info (connection, $"Sent message: {message}");

                if (pieceMessage != null) {
                    peerMonitor?.ProtocolUp.AddDelta (-pieceMessage.RequestLength);
                    managerMonitor?.ProtocolUp.AddDelta (-pieceMessage.RequestLength);

                    peerMonitor?.DataUp.AddDelta (pieceMessage.RequestLength);
                    managerMonitor?.DataUp.AddDelta (pieceMessage.RequestLength);
                }
            } finally {
                ClientEngine.BufferPool.Return (buffer);
            }
        }
    }
}
