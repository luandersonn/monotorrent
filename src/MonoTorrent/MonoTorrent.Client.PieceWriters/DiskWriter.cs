//
// DiskWriter.cs
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
using System.IO;
using System.Threading.Tasks;

namespace MonoTorrent.Client.PieceWriters
{
    // File is not used in Torrent RT because System.IO.File API does not work properly in UWP.
    // See MonoTorrent.UWP\DiskWriter.cs instead.
    public class DiskWriter : IPieceWriter
    {
        readonly FileStreamBuffer streamsBuffer;

        public int OpenFiles => streamsBuffer.Count;

        public DiskWriter ()
            : this (10)
        {

        }

        public DiskWriter (int maxOpenFiles)
        {
            streamsBuffer = new FileStreamBuffer (maxOpenFiles);
        }

        public void Close (TorrentFile file)
        {
            streamsBuffer.CloseStream (file.FullPath);
        }

        public void Dispose ()
        {
            streamsBuffer.Dispose ();
        }

        async Task<TorrentFileStream> GetStreamAsync(TorrentFile file, FileAccess access)
        {
            return await streamsBuffer.GetStreamAsync(file, access);
        }

        public async Task MoveAsync(TorrentFile file, string newPath, bool overwrite)
        {
            streamsBuffer.CloseStream (file.FullPath);
            if (overwrite)
                File.Delete (newPath);
            File.Move (file.FullPath, newPath);
        }

        public async Task<int> ReadAsync(TorrentFile file, long offset, byte[] buffer, int bufferOffset, int count)
        {
            Check.File (file);
            Check.Buffer (buffer);

            if (offset < 0 || offset + count > file.Length)
                throw new ArgumentOutOfRangeException (nameof (offset));

            TorrentFileStream s = await GetStreamAsync(file, FileAccess.Read);
            if (s.Length < offset + count)
                return 0;

            if (s.Position != offset)
                s.Seek(offset, SeekOrigin.Begin);
            return await s.ReadAsync(buffer, bufferOffset, count);
        }

        public async Task WriteAsync(TorrentFile file, long offset, byte[] buffer, int bufferOffset, int count)
        {
            Check.File (file);
            Check.Buffer (buffer);

            if (offset < 0 || offset + count > file.Length)
                throw new ArgumentOutOfRangeException (nameof (offset));

            TorrentFileStream stream = await GetStreamAsync(file, FileAccess.ReadWrite);
            stream.Seek(offset, SeekOrigin.Begin);
            await stream.WriteAsync(buffer, bufferOffset, count);
        }

        public async Task<bool> ExistsAsync(TorrentFile file)
        {
            return File.Exists(file.FullPath); // will not work
        }

        public async Task FlushAsync(TorrentFile file)
        {
            Stream s = streamsBuffer.FindStream (file.FullPath);
            s?.Flush ();
        }
    }
}
