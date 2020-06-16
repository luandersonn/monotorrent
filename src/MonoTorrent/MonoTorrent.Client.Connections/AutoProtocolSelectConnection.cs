using MonoTorrent.Client.Connections;
using ReusableTasks;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace MonoTorrent.Client.Connections
{
    /// <summary>
    /// Class tho select between TCP (socket connection) and UTP.
    /// </summary>
    class AutoProtocolSelectConnection : IConnection2
    {
        private IConnection2 selectedProtocolConnection;

        public byte[] AddressBytes => selectedProtocolConnection.AddressBytes;

        public bool Connected => selectedProtocolConnection.Connected;

        public bool CanReconnect => selectedProtocolConnection.CanReconnect;

        public bool IsIncoming => false; // we don't support incoming UTP connections

        public EndPoint EndPoint => selectedProtocolConnection.EndPoint;

        public Uri Uri { get; private set; }

        private EnabledProtocolTypes enabledProtocolType;

        private IConnection2 GetOriginalProtocolConnection()
        {
            switch (this.enabledProtocolType)
            {
                case EnabledProtocolTypes.TCP:
                    return new IPV4Connection(this.Uri);
                case EnabledProtocolTypes.TCPtoUTP:
                    return new IPV4Connection(this.Uri);
                case EnabledProtocolTypes.UTP:
                    return new UTPConnection(this.Uri);
                case EnabledProtocolTypes.UTPtoTCP:
                    return new UTPConnection(this.Uri);
                default:
                    throw new ArgumentException("unsupported value: " + this.enabledProtocolType);
            }
        }
        private IConnection2 GetBackupProtocolConnection()
        {
            switch (this.enabledProtocolType)
            {
                case EnabledProtocolTypes.TCPtoUTP:
                    return new UTPConnection(this.Uri);
                case EnabledProtocolTypes.UTPtoTCP:
                    return new IPV4Connection(this.Uri);
                default:
                    return null;
            }
        }

        public AutoProtocolSelectConnection(Uri uri, EnabledProtocolTypes enabledProtocolType)
        {
            this.Uri = uri;
            this.enabledProtocolType = enabledProtocolType;
            this.selectedProtocolConnection = GetOriginalProtocolConnection();
        }

        async Task IConnection.ConnectAsync()
            => await ConnectAsync();

        public async ReusableTask ConnectAsync()
        {
            try
            {
                await this.selectedProtocolConnection.ConnectAsync();
            }
            catch
            {
                this.selectedProtocolConnection.SafeDispose();
                selectedProtocolConnection = GetBackupProtocolConnection();
                if (selectedProtocolConnection == null)
                {
                    return;
                }
                await this.selectedProtocolConnection.ConnectAsync();
            }
        }

        public void Dispose()
        {
            selectedProtocolConnection.Dispose();
        }

        async Task<int> IConnection.ReceiveAsync(byte[] buffer, int offset, int count)
            => await ReceiveAsync(buffer, offset, count);

        public ReusableTask<int> ReceiveAsync(byte[] buffer, int offset, int count)
        {
            return selectedProtocolConnection.ReceiveAsync(buffer, offset, count);
        }

        async Task<int> IConnection.SendAsync(byte[] buffer, int offset, int count)
            => await SendAsync(buffer, offset, count);

        public ReusableTask<int> SendAsync(byte[] buffer, int offset, int count)
        {
            return selectedProtocolConnection.SendAsync(buffer, offset, count);
        }
    }
}
