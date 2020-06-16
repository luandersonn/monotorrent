using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Search;

using Path = System.IO.Path;
using FileNotFoundException = System.IO.FileNotFoundException;

namespace MonoTorrent.UWP
{
    public class Directory
    {
        // Make sure that this is same as the one in DownloadsFolderHelper, so I don't have to refactor it.
        //private const string DownloadsFolderFutureAccessListToken = "DownloadsFolder";
        //private static readonly string DownloadFolderPathSubString = "Downloads\\" + Package.Current.Id.FamilyName + "!App";

        public static async Task<StorageFolder> TryGetStorageFolderAsync(string path)
        {
            // Important: path can only be in a form of Downloads\325289AEDD75.TorrentRT_qtx9tqphctw9r!App\Downloads
            // Trying accessing Downloads\Torrent RT\Downloads will throw UnauthorizedAccessException

            // HACK: this has implicit dependency on DownloadsFolderHelper.
            //if (path.Contains(DownloadFolderPathSubString) && StorageApplicationPermissions.FutureAccessList.ContainsItem(DownloadsFolderFutureAccessListToken))
            //{
            //    StorageFolder defaultDownloadsFolder = await StorageApplicationPermissions.FutureAccessList.GetFolderAsync(DownloadsFolderFutureAccessListToken);
            //    if (path.Equals(defaultDownloadsFolder.Path, StringComparison.OrdinalIgnoreCase))
            //    {
            //        return defaultDownloadsFolder;
            //    }
            //    if (path.StartsWith(defaultDownloadsFolder.Path, StringComparison.OrdinalIgnoreCase))
            //    {
            //        string relativePath = path.Substring(defaultDownloadsFolder.Path.Length).TrimStart(Path.DirectorySeparatorChar);
            //        StorageFolder storageFolder = await defaultDownloadsFolder.TryGetItemAsync(relativePath) as StorageFolder;
            //        return storageFolder;
            //    }
            //}

             var futureAccessListToken = FolderPathToFutureAccessListToken(path);

            if (StorageApplicationPermissions.FutureAccessList.ContainsItem(futureAccessListToken))
            {
                return await StorageApplicationPermissions.FutureAccessList.GetFolderAsync(futureAccessListToken);
            }

            try
            {
                return await StorageFolder.GetFolderFromPathAsync(path);
            }
            catch (AggregateException aggregateException)
            {
                if (aggregateException.InnerException is FileNotFoundException)
                {
                    return null;
                }

                throw; // check if it is right thing to do
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (System.IO.IOException)
            {
                return null;
            }
            catch (System.UnauthorizedAccessException)
            {
                Debugger.Break();
                throw;
            }
            catch
            {
                throw;
            }
        }

        public static async Task<bool> ExistsAsync(string path)
        {
            //return System.IO.Directory.Exists(path); // does not work
            var result = await TryGetStorageFolderAsync(path);
            return result != null;
        }

        public static async Task CreateDirectoryAsync(string path)
        {
            await CreateDirectoryIfNotExistsAsync(path);
        }

        async public static Task<StorageFolder> CreateDirectoryIfNotExistsAsync(string path)
        {
            Debug.Assert(path != null);
            Debug.Assert(Path.IsPathRooted(path));
            StorageFolder baseFolder = await FindExistingBaseFolderAsync(path);
            string subFolders = PathHelpers.RemovePrefixPath(path, baseFolder.Path);
            return await CreateFoldersStructureAsync(baseFolder, subFolders);
        }

        async private static Task<StorageFolder> FindExistingBaseFolderAsync(string path)
        {
            StorageFolder storageFolder = await TryGetStorageFolderAsync(path);
            if (storageFolder != null)
            {
                return storageFolder;
            }
            else
            {
                return await FindExistingBaseFolderAsync(Path.GetDirectoryName(path));
            }
        }

        async public static Task<StorageFolder> CreateFoldersStructureAsync(StorageFolder baseFolder, string subFolders)
        {
            subFolders = subFolders.TrimStart('\\').TrimEnd('\\');
            if (String.IsNullOrEmpty(subFolders))
            {
                return baseFolder;
            }

            string[] subFoldersArray = subFolders.Split('\\');

            foreach (string subFolder in subFoldersArray)
            {
                baseFolder = await baseFolder.CreateFolderAsync(subFolder, CreationCollisionOption.OpenIfExists);
            }

            return baseFolder;
        }

        public static async Task DeleteAsync(string path)
        {
            var folder = await TryGetStorageFolderAsync(path);
            if (folder != null)
            {
                await folder.DeleteAsync();
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

        public static string FolderPathToFutureAccessListToken(string path)
        {
            return path?.ToLowerInvariant().Replace(':', '_').Replace('\\', '_');
        }
    }
}
