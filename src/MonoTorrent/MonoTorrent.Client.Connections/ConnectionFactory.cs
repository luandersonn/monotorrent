//
// ConnectionFactory.cs
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
using System.Collections.Generic;

namespace MonoTorrent.Client.Connections
{
    public static class ConnectionFactory
    {
        static ConnectionFactory()
        {
        }

        private static IConnection2 GetOriginalIpV4ProtocolConnection (Uri connectionUri, EnabledProtocolTypes enabledProtocolType)
        {
            switch (enabledProtocolType) {
                case EnabledProtocolTypes.TCP:
                case EnabledProtocolTypes.TCPtoUTP:
                    return new IPV4Connection (connectionUri);
                case EnabledProtocolTypes.UTP:
                case EnabledProtocolTypes.UTPtoTCP:
                    return new UTPConnection (connectionUri);
                default:
                    throw new ArgumentException ("unsupported value: " + enabledProtocolType);
            }
        }

        public static IConnection2 GetFallbackProtocolConnection(Uri connectionUri, ProtocolTypes currentProtocolType, EnabledProtocolTypes enabledProtocolTypes)
        {
            if (currentProtocolType == ProtocolTypes.TCP && enabledProtocolTypes == EnabledProtocolTypes.TCPtoUTP) {
                return new UTPConnection (connectionUri);
            }
            else if (currentProtocolType == ProtocolTypes.uTP && enabledProtocolTypes == EnabledProtocolTypes.UTPtoTCP) {
                return new IPV4Connection (connectionUri);
            }

            return null;
        }

        public static IConnection Create(Uri connectionUri, EnabledProtocolTypes enabledProtocolType)
        {
            if (connectionUri == null)
                throw new ArgumentNullException(nameof (connectionUri));

            if (connectionUri.Scheme == "ipv4" && connectionUri.Port == -1)
                return null;

            switch (connectionUri.Scheme)
            {
                //case "ipv4": return new AutoProtocolSelectConnection(connectionUri, enabledProtocolType);
               //case "ipv4": return new IPV4Connection(connectionUri);
                case "ipv4": return GetOriginalIpV4ProtocolConnection (connectionUri, enabledProtocolType);
                case "ipv6": return new IPV6Connection(connectionUri);
                case "http": return new HttpConnection(connectionUri);
                default: return null;
            }
        }
    }
}
