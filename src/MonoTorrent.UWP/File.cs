using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Diagnostics;

using FileNotFoundException = System.IO.FileNotFoundException;
using Path = System.IO.Path;

namespace MonoTorrent.UWP
{
    public static class File
    {
        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        public static async Task SafeDeleteFileAsync(this StorageFolder folder, string fileName)
        {
            try
            {
                var file = await folder.TryGetItemAsync(fileName) as StorageFile;
                if (file != null)
                {
                    await file.DeleteAsync();
                }
            }
            catch { }
        }

        public static async Task<StorageFile> GetStorageFileAsync(string path)
        {
            if (path.StartsWith(ApplicationData.Current.LocalFolder.Path))
            {
                // TODOD: remove this clause
                Debugger.Break();
                path = PathHelpers.RemovePrefixPath(path, ApplicationData.Current.LocalFolder.Path);
                return await ApplicationData.Current.LocalFolder.GetFileAsync(path);
            }

            if (Path.IsPathRooted(path))
            {
                return await StorageFile.GetFileFromPathAsync(path);
            }

            Debugger.Break();
            // TODOD: remove this clause
            var file = await ApplicationData.Current.LocalFolder.TryGetItemAsync(path);
            return file as StorageFile;
        }

        public static async Task<bool> ExistsAsync(string path)
        {
            try
            {
                var file = await File.GetStorageFileAsync(path);
                return file != null;
            }
            catch (AggregateException aggregateException)
            {
                if (aggregateException.InnerException is FileNotFoundException)
                {
                    return false;
                }

                logger.Error(aggregateException.InnerException, "File.Exists unexpected exception");

                throw; // check if it is right thing to do
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (System.IO.IOException)
            {
                return false;
            }
            catch (System.UnauthorizedAccessException)
            {
                return false;
            }
            catch
            {
                throw;
            }
        }

        public static async Task MoveAsync(string oldPath, string newPath)
        {
            var fileTask = await GetStorageFileAsync(oldPath);
            var folderTask = await StorageFolder.GetFolderFromPathAsync(newPath);

            await fileTask.MoveAsync(folderTask);
        }

        public static async Task DeleteAsync(string path)
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            if (file != null)
            {
                await file.DeleteAsync();
            }
        }

        public static async Task SafeDeleteAsync(string path)
        {
            try
            {
                await DeleteAsync(path);
            }
            catch { }
        }
    }
}
