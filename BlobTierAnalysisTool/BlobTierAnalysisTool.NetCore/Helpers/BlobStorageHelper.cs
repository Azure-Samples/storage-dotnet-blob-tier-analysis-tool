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
        private static CloudBlobClient _blobClient;

        /// <summary>
        /// Tries to parse a connection string and sets the storage account.
        /// </summary>
        /// <param name="connectionString">Storage account connection string.</param>
        /// <returns>True or false</returns>
        public static bool ParseConnectionString(string connectionString)
        {
            try
            {
                Uri sasUri = null;
                if (Uri.TryCreate(connectionString, UriKind.Absolute, out sasUri))
                {
                    var sasToken = sasUri.Query;
                    var baseUrl = sasUri.AbsoluteUri.Replace(sasToken, "");
                    _blobClient = new CloudBlobClient(new Uri(baseUrl), new StorageCredentials(sasToken));
                    return true;
                }
                else
                {
                    CloudStorageAccount _storageAccount;
                    if (CloudStorageAccount.TryParse(connectionString, out _storageAccount))
                    {
                        _blobClient = _storageAccount.CreateCloudBlobClient();
                        return true;
                    }

                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if the container specified by the user exists or not.
        /// </summary>
        /// <param name="containerName">Name of the container.</param>
        /// <returns>True if container exists else false.</returns>
        public static async Task<Tuple<bool, bool>> DoesContainerExists(string containerName)
        {
            bool isValidConnection = true;
            try
            {
                bool doesContainerExist = await _blobClient.GetContainerReference(containerName).ExistsAsync();
                return new Tuple<bool, bool>(doesContainerExist, isValidConnection);
            }
            catch (StorageException exception)
            {
                if (exception.RequestInformation.HttpStatusCode == 403)
                {
                    isValidConnection = false;
                }
                return new Tuple<bool, bool>(false, isValidConnection);
            }
        }

        /// <summary>
        /// Validates the connection to the storage account. This method will try to perform list containers
        /// operation (fetching just one container) if <paramref name="containerName"/> parameter is not defined
        /// otherwise it will try to perform list blobs operation (fetching just one blob). The objective of this
        /// method is to capture 403 error.
        /// </summary>
        /// <param name="containerName"></param>
        /// <returns>True if connecting is validated else false.</returns>
        public static async Task<bool> ValidateConnection(string containerName = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(containerName) || containerName == "*")
                {
                    await _blobClient.ListContainersSegmentedAsync(null, ContainerListingDetails.None, 1, null, null, null);
                }
                else
                {
                    var blobContainer = _blobClient.GetContainerReference(containerName);
                    await blobContainer.ListBlobsSegmentedAsync(null, true, BlobListingDetails.None, 1, null, null, null);
                }
                return true;
            }
            catch (StorageException exception)
            {
                if (exception.RequestInformation.HttpStatusCode == 403)
                {
                    return false;
                }
                return true;
            }
        }

        /// <summary>
        /// List blob containers in a storage account.
        /// </summary>
        /// <returns>List of blob containers.</returns>
        public static async Task<IEnumerable<string>> ListContainers()
        {
            List<string> containers = new List<string>();
            BlobContinuationToken token = null;
            do
            {
                var result = await _blobClient.ListContainersSegmentedAsync(token);
                token = result.ContinuationToken;
                var blobContainers = result.Results.Select(blobContainer => blobContainer.Name); ;
                containers.AddRange(blobContainers);
            }
            while (token != null);
            return containers;
        }

        /// <summary>
        /// Analyzes blobs in a container. This method will list all blobs in a blob container and 
        /// matches them against the filter criteria.
        /// </summary>
        /// <param name="containerName">Name of the blob container.</param>
        /// <param name="filterCriteria"><see cref="Models.FilterCriteria"/></param>
        /// <returns>
        /// <see cref="Models.ContainerStatistics"/> which contains information about block blobs
        /// in different tiers and puts them in 2 buckets - all block blobs & block blobs that match
        /// the filter criteria.
        /// </returns>
        public static async Task<Models.ContainerStatistics> AnalyzeContainer(string containerName, Models.FilterCriteria filterCriteria)
        {
            var blobContainer = _blobClient.GetContainerReference(containerName);
            var containerStats = new Models.ContainerStatistics(containerName);
            BlobContinuationToken token = null;
            try
            {
                do
                {
                    var result = await blobContainer.ListBlobsSegmentedAsync(null, true, BlobListingDetails.None, 250, token, null, null);
                    token = result.ContinuationToken;
                    var blobs = result.Results;
                    foreach (var blob in blobs)
                    {
                        var cloudBlockBlob = blob as CloudBlockBlob;
                        if (cloudBlockBlob != null)
                        {
                            long blobSize = cloudBlockBlob.Properties.Length;
                            DateTime blobLastModifiedDate = cloudBlockBlob.Properties.LastModified.Value.DateTime;
                            var doesBlobMatchFilterCriteria = DoesBlobMatchFilterCriteria(cloudBlockBlob, filterCriteria);
                            var blobTier = cloudBlockBlob.Properties.StandardBlobTier;
                            switch (blobTier)
                            {
                                case null:
                                case StandardBlobTier.Hot:
                                    var hotAccessTierStats = containerStats.BlobsStatistics[StandardBlobTier.Hot];
                                    hotAccessTierStats.Count += 1;
                                    hotAccessTierStats.Size += blobSize;
                                    if (doesBlobMatchFilterCriteria)
                                    {
                                        var matchingHotAccessTierStats = containerStats.MatchingBlobsStatistics[StandardBlobTier.Hot];
                                        matchingHotAccessTierStats.Count += 1;
                                        matchingHotAccessTierStats.Size += blobSize;
                                        matchingHotAccessTierStats.BlobNames.Add(cloudBlockBlob.Name);
                                    }
                                    break;
                                case StandardBlobTier.Cool:
                                    var coolAccessTierStats = containerStats.BlobsStatistics[StandardBlobTier.Cool];
                                    coolAccessTierStats.Count += 1;
                                    coolAccessTierStats.Size += blobSize;
                                    if (doesBlobMatchFilterCriteria)
                                    {
                                        var matchingCoolAccessTierStats = containerStats.MatchingBlobsStatistics[StandardBlobTier.Cool];
                                        matchingCoolAccessTierStats.Count += 1;
                                        matchingCoolAccessTierStats.Size += blobSize;
                                        matchingCoolAccessTierStats.BlobNames.Add(cloudBlockBlob.Name);
                                    }
                                    break;
                                case StandardBlobTier.Archive:
                                    var archiveAccessTierStats = containerStats.BlobsStatistics[StandardBlobTier.Archive];
                                    archiveAccessTierStats.Count += 1;
                                    archiveAccessTierStats.Size += blobSize;
                                    if (doesBlobMatchFilterCriteria)
                                    {
                                        var matchingArchiveAccessTierStats = containerStats.MatchingBlobsStatistics[StandardBlobTier.Archive];
                                        matchingArchiveAccessTierStats.Count += 1;
                                        matchingArchiveAccessTierStats.Size += blobSize;
                                        matchingArchiveAccessTierStats.BlobNames.Add(cloudBlockBlob.Name);
                                    }
                                    break;
                            }
                        }
                    }
                }
                while (token != null);
            }
            catch (Exception exception)
            {

            }
            return containerStats;
        }

        /// <summary>
        /// Changes the access tier of a block blob in a blob container.
        /// </summary>
        /// <param name="containerName">Name of the blob container.</param>
        /// <param name="blobName">Name of the blob.</param>
        /// <param name="targetTier"><see cref="StandardBlobTier"/> which indicates the new access tier for the blob.</param>
        /// <returns>True or false.</returns>
        public static async Task<bool> ChangeAccessTier(string containerName, string blobName, StandardBlobTier targetTier)
        {
            try
            {
                CloudBlockBlob blob = _blobClient.GetContainerReference(containerName).GetBlockBlobReference(blobName);
                await blob.SetStandardBlobTierAsync(targetTier);
                return true;
            }
            catch (Exception exception)
            {
                return false;
            }
        }

        public static string StorageAccountName
        {
            get
            {
                return _blobClient.Credentials.AccountName;
            }
        }

        /// <summary>
        /// Checks the blob to see if it can be archived.
        /// </summary>
        /// <param name="blob"></param>
        /// <param name="filterCriteria"></param>
        /// <returns></returns>
        private static bool DoesBlobMatchFilterCriteria(CloudBlockBlob blob, Models.FilterCriteria filterCriteria)
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
