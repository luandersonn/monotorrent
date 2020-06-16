//
// FileStreamBuffer.cs
//
// Authors:
//   Alan McGovern alan.mcgovern@gmail.com
//
// Copyright (C) 2009 Alan McGovern
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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MonoTorrent.Client;
using Windows.Storage;
using LruCacheNet;

namespace MonoTorrent.UWP.Client
{
    class FileStreamBuffer : IDisposable
    {
        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        private const int bufferSize = 16384;
        // A list of currently open filestreams. Note: The least recently used is at position 0
        // The most recently used is at the last position in the array
        private List<TorrentFileStream> list;
        private Dictionary<string, int> openCloseCount = new Dictionary<string, int>(); //used for debugging
        private int maxStreams;
        LruCache<string, StorageFile> storageFileLru = new LruCache<string, StorageFile>(100);
        LruCache<string, StorageFolder> storageFolderLru = new LruCache<string, StorageFolder>(100);

        public int Count
		{
			get { return list.Count; }
		}
		
		public List<TorrentFileStream> Streams
		{
			get { return list; }
		}

        public FileStreamBuffer(int maxStreams)
        {
            this.maxStreams = maxStreams;
            list = new List<TorrentFileStream>(maxStreams);
        }

        private void Add(TorrentFileStream stream)
        {
            logger.Info ("Opening filestream: {0}", stream.Path);

            // If we have our maximum number of streams open, just dispose and dump the least recently used one
            if (maxStreams != 0 && list.Count >= list.Capacity)
            {
                logger.Info ("We've reached capacity: {0}", list.Count);
                CloseAndRemove(list[0]);
            }
            list.Add(stream);
        }

        public TorrentFileStream FindStream(string path)
        {
            // LRU cache implementation
            for (int i = 0; i < list.Count; i++)
                if (list[i].Path == path)
                    return list[i];
            return null;
        }

        public string GetStreamsAsString()
        {
            return String.Join('\n', list.Select(t => t.Path).ToArray());
        }

        internal async Task<TorrentFileStream> GetStreamAsync(TorrentFile file, FileAccess access)
        {
            logger.Info($"Looking for a file stream named {file.FullPath}");
            TorrentFileStream s = FindStream(file.FullPath);

            if (s != null)
            {
                // If we are requesting write access and the current stream does not have it
                if (((access & FileAccess.Write) == FileAccess.Write) && !s.CanWrite)
                {
                    logger.Info($"Didn't have write permission to {file.FullPath}  - reopening");
                    CloseAndRemove(s);
                    s = null;
                }
                else
                {
                    // Place the filestream at the end so we know it's been recently used
                    list.Remove(s);
                    list.Add(s);
                    return s;
                }
            }

            if (s == null)
            {
                logger.Info($"Opening file: {file.FullPath} access: {access}");

                try
                {
                    StorageFile storageFile = await GetStorageFile(file);

                    var randomAccessStream = await storageFile.OpenAsync(
                        access == FileAccess.Read ? FileAccessMode.Read : FileAccessMode.ReadWrite,
                        StorageOpenOptions.AllowOnlyReaders);
                    var stream = randomAccessStream.AsStream(bufferSize);

                    if (stream.Length > file.Length)
                    {
                        if (!stream.CanWrite)
                        {
                            stream.Close();
                            access = FileAccess.ReadWrite;
                            randomAccessStream = await storageFile.OpenAsync(
                                access == FileAccess.Read ? FileAccessMode.Read : FileAccessMode.ReadWrite,
                                StorageOpenOptions.AllowOnlyReaders);
                            stream = randomAccessStream.AsStream(bufferSize);
                        }
                        stream.SetLength(file.Length);
                    }

                    s = new TorrentFileStream(
                        stream,
                        file,
                        FileMode.OpenOrCreate,
                        access,
                        FileShare.Read);
                    UpdateOpenCloseCounter(file.FullPath, 1);

                    Add(s);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "FileStreamBuffer GetStreamAsync error");
                    //Debugger.Launch();
                    throw;
                }
            }

            return s;
        }

        private async Task<StorageFile> GetStorageFile(TorrentFile file)
        {
            StorageFile storageFile;
            if (!storageFileLru.ContainsKey(file.FullPath))
            {
                string folderPath = Path.GetDirectoryName(file.FullPath);
                StorageFolder parentStorageFolder;
                if (!storageFolderLru.ContainsKey(folderPath))
                {
                    parentStorageFolder = await Directory.CreateDirectoryIfNotExistsAsync(folderPath);
                    if (parentStorageFolder == null)
                    {
                        throw new IOException($"Can't create {folderPath} folder");
                    }
                    storageFolderLru.AddOrUpdate(folderPath, parentStorageFolder);
                }
                else
                {
                    parentStorageFolder = storageFolderLru.Get(folderPath);
                }

                storageFile = await parentStorageFolder.CreateFileAsync(Path.GetFileName(file.FullPath), CreationCollisionOption.OpenIfExists);
                if (storageFile == null)
                {
                    throw new IOException($"Can't create {file.FullPath} file");
                }
                storageFileLru.AddOrUpdate(file.FullPath, storageFile);
            }
            else
            {
                storageFile = storageFileLru.Get(file.FullPath);
            }

            return storageFile;
        }

        private void UpdateOpenCloseCounter(string path, int value)
        {
#if DEBUG
            if (!openCloseCount.ContainsKey(path))
            {
                openCloseCount[path] = value;
                return;
            }
            openCloseCount[path] += value;
#endif
        }

        #region IDisposable Members

        public void Dispose()
        {
            storageFileLru.Clear();
            storageFolderLru.Clear();
            list.ForEach(delegate (TorrentFileStream s) 
            {
                UpdateOpenCloseCounter(s.Path, -1);
                s.Close();
            }); 
            list.Clear();
        }

        #endregion

        internal bool CloseStream(string path)
        {
            TorrentFileStream s = FindStream(path);
            if (s != null)
                CloseAndRemove(s);

            return s != null;
        }

        private void CloseAndRemove(TorrentFileStream s)
        {
            UpdateOpenCloseCounter(s.Path, -1);
            logger.Info("Closing and removing: {0}", s.Path);
            if (s.File.Priority == Priority.DoNotWriteToDisk || s.File.Priority == Priority.DoNotDownload)
            {
                storageFileLru.Remove(s.File.FullPath);
                storageFolderLru.Remove(Path.GetDirectoryName(s.File.FullPath));
            }

            list.Remove(s);
            s.Close();
            //s.Dispose();
        }
    }
}
