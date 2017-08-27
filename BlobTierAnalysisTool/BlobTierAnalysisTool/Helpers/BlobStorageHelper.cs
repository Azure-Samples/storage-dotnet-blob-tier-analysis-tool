using System;
using System.Configuration;
using System.Threading.Tasks;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace BlobTierAnalysisTool.Helpers
{
    public static class BlobStorageHelper
    {
        private static CloudStorageAccount _storageAccount;

        public static async Task<IEnumerable<string>> ListContainers()
        {
            List<string> containers = new List<string>();
            var blobClient = StorageAccount.CreateCloudBlobClient();
            BlobContinuationToken token = null;
            do
            {
                var result = await blobClient.ListContainersSegmentedAsync(token);
                token = result.ContinuationToken;
                var blobContainers = result.Results.Select(blobContainer => blobContainer.Name); ;
                containers.AddRange(blobContainers);
            }
            while (token != null);
            return containers;
        }

        public static async Task<Models.ContainerStatistics> AnalyzeContainerForArchival(string containerName, Models.FilterCriteria filterCriteria)
        {
            var blobContainer = StorageAccount.CreateCloudBlobClient().GetContainerReference(containerName);
            var containerStats = new Models.ContainerStatistics(containerName);
            BlobContinuationToken token = null;
            do
            {
                var result = await blobContainer.ListBlobsSegmentedAsync(null, true, BlobListingDetails.None, 100, token, null, null);
                token = result.ContinuationToken;
                var blobs = result.Results;
                foreach (var blob in blobs)
                {
                    var cloudBlockBlob = blob as CloudBlockBlob;
                    if (cloudBlockBlob != null)
                    {
                        long blobSize = cloudBlockBlob.Properties.Length;
                        DateTime blobLastModifiedDate = cloudBlockBlob.Properties.LastModified.Value.DateTime;
                        var canBlobBeArchived = CanBlobBeArchived(cloudBlockBlob, filterCriteria);
                        var blobTier = cloudBlockBlob.Properties.StandardBlobTier;
                        switch (blobTier)
                        {
                            case null:
                            case StandardBlobTier.Hot:
                                var hotAccessTierStats = containerStats.BlobsStatistics[StandardBlobTier.Hot];
                                hotAccessTierStats.Count += 1;
                                hotAccessTierStats.Size += blobSize;
                                if (canBlobBeArchived)
                                {
                                    var hotArchivableAccessTierStats = containerStats.ArchivableBlobsStatistics[StandardBlobTier.Hot];
                                    hotArchivableAccessTierStats.Count += 1;
                                    hotArchivableAccessTierStats.Size += blobSize;
                                    hotArchivableAccessTierStats.BlobNames.Add(cloudBlockBlob.Name);
                                }
                                break;
                            case StandardBlobTier.Cool:
                                var coolAccessTierStats = containerStats.BlobsStatistics[StandardBlobTier.Cool];
                                coolAccessTierStats.Count += 1;
                                coolAccessTierStats.Size += blobSize;
                                if (canBlobBeArchived)
                                {
                                    var coolArchivableAccessTierStats = containerStats.ArchivableBlobsStatistics[StandardBlobTier.Cool];
                                    coolArchivableAccessTierStats.Count += 1;
                                    coolArchivableAccessTierStats.Size += blobSize;
                                    coolArchivableAccessTierStats.BlobNames.Add(cloudBlockBlob.Name);
                                }
                                break;
                            case StandardBlobTier.Archive:
                                var archiveAccessTierStats = containerStats.BlobsStatistics[StandardBlobTier.Archive];
                                archiveAccessTierStats.Count += 1;
                                archiveAccessTierStats.Size += blobSize;
                                break;
                        }
                    }
                }
            }
            while (token != null);
            return containerStats;
        }

        public static async Task<bool> ChangeAccessTier(string containerName, string blobName, StandardBlobTier targetTier)
        {
            try
            {
                var cloudBlockBlob = StorageAccount.CreateCloudBlobClient().GetContainerReference(containerName).GetBlockBlobReference(blobName);
                await cloudBlockBlob.SetStandardBlobTierAsync(targetTier);
                return true;
            }
            catch (Exception exception)
            {
                return false;
            }
        }

        public static CloudStorageAccount StorageAccount
        {
            get
            {
                if (_storageAccount != null) return _storageAccount;
                var appSettingsReader = new AppSettingsReader();
                var connectionString = (String)appSettingsReader.GetValue("StorageConnectionString", typeof(String));
                _storageAccount = CloudStorageAccount.Parse(connectionString);
                return _storageAccount;
            }
        }

        /// <summary>
        /// Checks the blob to see if it can be archived.
        /// </summary>
        /// <param name="blob"></param>
        /// <param name="filterCriteria"></param>
        /// <returns></returns>
        private static bool CanBlobBeArchived(CloudBlockBlob blob, Models.FilterCriteria filterCriteria)
        {
            if (blob.Properties.StandardBlobTier == StandardBlobTier.Archive) return false;
            var dateTimeFrom = filterCriteria.LastModifiedDateFrom ?? DateTime.MinValue;
            var dateTimeTo = filterCriteria.LastModifiedDateTo ?? DateTime.MaxValue;
            var minBlobSize = filterCriteria.MinBlobSize;
            bool isDateTimeCheckPassed = false;
            if (blob.Properties.LastModified.HasValue)
            {
                var lastModified = blob.Properties.LastModified.Value.DateTime;
                if (dateTimeFrom <= lastModified && dateTimeTo >= lastModified)
                {
                    isDateTimeCheckPassed = true;
                }
            }
            bool isBlobSizeCheckPassed = blob.Properties.Length >= minBlobSize;
            return isDateTimeCheckPassed || isBlobSizeCheckPassed;
        }
    }
}
