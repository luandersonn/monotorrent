using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

// using MonoTorrent.Analytics;

namespace MonoTorrent.Client
{
    // TODO: alekseyv: mov it out
    public static class AnalyticsLogger
    {
        private const int DefaultConnectionBatchSize = 100;
        //private static List<IAnalyticsTracker> listeners = new List<IAnalyticsTracker>();
        
        static AnalyticsLogger()
        {
            //listeners = new List<IAnalyticsTracker>();
        }

        //public static void AddListener(IAnalyticsTracker listener)
        //{
        //    if (listener == null)
        //        throw new ArgumentNullException("listener");

        //    lock (listeners)
        //    {
        //        if (!listeners.Contains(listener))
        //        {
        //            listeners.Add(listener);
        //        }
        //    }
        //}

        private static void SendEvent(string category, string action, string label, int value)
        {
           // listeners.ForEach(l => l.SendEvent(category, action, label, value));
        }

        public static void SendEventBatch(string category, string action, string label, int batchSize)
        {
            //listeners.ForEach(l => l.SendEventBatch(category, action, label, batchSize));
        }

        //public static void SendOutcomingConnectionEventBatch(string action, PeerId peerId)
        //{
        //    if (listeners.Count == 0)
        //    {
        //        return;
        //    }

        //    String label = "";
        //    if (peerId != null)
        //    {
        //        Software software = peerId.ClientApp;
        //        if (software.Client == Common.Client.Unknown && String.IsNullOrEmpty(software.ShortId))
        //        {
        //            software = new Software(peerId.PeerID);
        //        }

        //        label += software.ToString();

        //        if (peerId.Connection != null && peerId.Connection.Uri != null)
        //        {
        //            label += "_" + peerId.Connection.Uri.Scheme.ToString();
        //        }
        //    }
        //    else
        //    {
        //        label = "N/A";
        //    }
        //    SendEventBatch("incoming_connection", action, label, DefaultConnectionBatchSize);
        //}

        //public static void SendIncomingConnectionEventBatch(string action, Peer peer)
        //{
        //    if (listeners.Count == 0)
        //    {
        //        return;
        //    }

        //    String label = "";
        //    if (peer != null)
        //    {
        //        Software software = new Software(peer.PeerId);
        //        label += software.ToString();

        //        if (peer.ConnectionUri != null)
        //        {
        //            label += "_" + peer.ConnectionUri.Scheme.ToString();
        //        }
        //    }
        //    else
        //    {
        //        label = "N/A";
        //    }
        //    SendEventBatch("incoming_connection", action, label, DefaultConnectionBatchSize);
        //}
    }
}
