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


using MonoTorrent.Client;
using MonoTorrent.Client.PieceWriters;
using System;
using System.IO;
using System.Threading.Tasks;
using Nito.AsyncEx;
using System.Threading;
using System.Diagnostics;

namespace MonoTorrent.UWP.Client.PieceWriters
{
    public class DiskWriter : IPieceWriter
    {
        private FileStreamBuffer streamsBuffer;
        private readonly AsyncLock mutex = new AsyncLock();

        public int OpenFiles
        {
            get { return streamsBuffer.Count; }
        }

        public DiskWriter()
            : this(20)
        {

        }

        public DiskWriter(int maxOpenFiles)
        {
            streamsBuffer = new FileStreamBuffer(maxOpenFiles);
        }

        public void Close(TorrentFile file)
        {
            streamsBuffer.CloseStream(file.FullPath);
        }

        public void Dispose()
        {
            streamsBuffer.Dispose();
        }

        async Task<TorrentFileStream> GetStreamAsync(TorrentFile file, FileAccess access)
        {
            return await streamsBuffer.GetStreamAsync(file, access);
        }

        public async Task MoveAsync(TorrentFile file, string newPath, bool overwrite)
        {
            using (await mutex.LockAsync())
            {
                streamsBuffer.CloseStream(file.FullPath);
                if (overwrite)
                    await File.SafeDeleteAsync(newPath);
                await File.MoveAsync(file.FullPath, newPath);
            }
        }

        public async Task<int> ReadAsync(TorrentFile file, long offset, byte[] buffer, int bufferOffset, int count)
        {
            CheckFile(file);
            CheckBuffer(buffer);

            if (offset < 0 || offset + count > file.Length)
                throw new ArgumentOutOfRangeException("offset");

            using (await mutex.LockAsync())
            {
                var stream = await GetStreamAsync(file, FileAccess.Read);
                if (stream.Length < offset + count)
                    return 0;

                stream.Seek(offset, SeekOrigin.Begin);
                // even with lock Async read/write is not stable.
                //var result = await stream.ReadAsync(buffer, bufferOffset, count);
                var result = stream.Read(buffer, bufferOffset, count);
                return result;
            }
        }

        public async Task WriteAsync(TorrentFile file, long offset, byte[] buffer, int bufferOffset, int count)
        {
            CheckFile(file);
            CheckBuffer(buffer);

            // alekseyv HACK - whenever torrent is stopped, and all tasks are cleared, MainLoop is likely to have Write operations queued.
            // And those operations will prevent files from being deleted. To make sure that file won't be written, change its priority to DoNotWriteToDisk.

            // Important - shouldn't use DoNotDownload for that because whenever 16k block overlaps couple of files, torrent has to download
            // part or all file that is not marked for download, otherwise hash won't match.
            if (file.Priority == Priority.DoNotWriteToDisk)
            {
                return;
            }

            if (offset < 0 || offset + count > file.Length)
                throw new ArgumentOutOfRangeException("offset");

            using (await mutex.LockAsync())
            {
                TorrentFileStream stream = await GetStreamAsync(file, FileAccess.ReadWrite);
                stream.Seek(offset, SeekOrigin.Begin);
                // even with lock Async read/write is not stable.
                //await stream.WriteAsync(buffer, bufferOffset, count);
                stream.Write(buffer, bufferOffset, count);
            }
        }

        public async Task<bool> ExistsAsync(TorrentFile file)
        {
            bool asyncExists = await File.ExistsAsync(file.FullPath);
            //bool systemioExists = System.IO.File.Exists(file.FullPath); // does not work
            return asyncExists;
        }

        public async Task FlushAsync(TorrentFile file)
        {
            using (await mutex.LockAsync())
            {
                var s = streamsBuffer.FindStream(file.FullPath);
                if (s != null)
                    await s.FlushAsync();
            }
        }

        private static void DoCheck(object toCheck, string name)
        {
            if (toCheck == null)
                throw new ArgumentNullException(name);
        }

        private static void CheckFile(object file)
        {
            DoCheck(file, "file");
        }

        private static void CheckBuffer(object buffer)
        {
            DoCheck(buffer, "buffer");
        }
    }
}
