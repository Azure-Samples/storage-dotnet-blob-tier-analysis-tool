using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BlobTierAnalysisTool.Helpers
{
    public static class BlobStorageHelper
    {
        private static BlobServiceClient s_blobServiceClient;

        /// <summary>
        /// Tries to parse a connection string and sets the storage account.
        /// </summary>
        /// <param name="connectionString">Storage account connection string.</param>
        /// <returns>True or false</returns>
        public static bool ParseConnectionString(string connectionString)
        {
            try
            {
                s_blobServiceClient = new BlobServiceClient(connectionString);
                return true;

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
        public static async Task<bool> DoesContainerExists(string containerName)
        {
                bool doesContainerExist = await s_blobServiceClient.GetBlobContainerClient(containerName).ExistsAsync();
                return doesContainerExist;
        }

        /// <summary>
        /// Validates the connection to the storage account. This method will try to perform list containers
        /// operation (fetching just one container) if <paramref name="containerName"/> parameter is not defined
        /// otherwise it will try to perform list blobs operation (fetching just one blob). The objective of this
        /// method is to capture 403 error.
        /// </summary>
        /// <param name="containerName"></param>
        /// <returns>True if connecting is validated else false.</returns>
        public static async Task<bool> ValidateConnectionAsync(string containerName = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(containerName) || containerName == "*")
                {
                    s_blobServiceClient.GetBlobContainers();
                }
                else
                {
                    BlobContainerClient blobContainer = s_blobServiceClient.GetBlobContainerClient(containerName);
                    await blobContainer.GetPropertiesAsync();
                }

                return true;
            }
            catch (RequestFailedException ex) when (ex.Status == 403)
            {
                return false;
            }
        }

        /// <summary>
        /// List blob containers in a storage account.
        /// </summary>
        /// <returns>List of blob containers.</returns>
        public static async Task<IEnumerable<string>> ListContainers()
        {
            List<string> containers = new List<string>();

            await foreach (var container in s_blobServiceClient.GetBlobContainersAsync())
            {
                containers.Add(container.Name);
            }

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
            BlobContainerClient blobContainer = s_blobServiceClient.GetBlobContainerClient(containerName);
            var containerStats = new Models.ContainerStatistics(containerName);
            try
            {
                await foreach (var blob in blobContainer.GetBlobsAsync())
                {
                    if (blob != null)
                    {

                        long blobSize = blob.Properties.ContentLength.GetValueOrDefault();

                        DateTime blobLastModifiedDate = blob.Properties.LastModified.Value.DateTime;
                        bool doesBlobMatchFilterCriteria = DoesBlobMatchFilterCriteriaAsync(blobContainer.GetBlobClient(blob.Name), filterCriteria).GetAwaiter().GetResult();
                        AccessTier? blobTier = blob.Properties.AccessTier;
                        switch (blobTier.Value.ToString())
                        {
                            case null:
                            case "Hot":
                                var hotAccessTierStats = containerStats.BlobsStatistics[AccessTier.Hot];
                                hotAccessTierStats.Count += 1;
                                hotAccessTierStats.Size += blobSize;
                                if (doesBlobMatchFilterCriteria)
                                {
                                    var matchingHotAccessTierStats = containerStats.MatchingBlobsStatistics[AccessTier.Hot];
                                    matchingHotAccessTierStats.Count += 1;
                                    matchingHotAccessTierStats.Size += blobSize;
                                    matchingHotAccessTierStats.BlobNames.Add(blob.Name);
                                }
                                break;
                            case "Cool":
                                var coolAccessTierStats = containerStats.BlobsStatistics[AccessTier.Cool];
                                coolAccessTierStats.Count += 1;
                                coolAccessTierStats.Size += blobSize;
                                if (doesBlobMatchFilterCriteria)
                                {
                                    var matchingCoolAccessTierStats = containerStats.MatchingBlobsStatistics[AccessTier.Cool];
                                    matchingCoolAccessTierStats.Count += 1;
                                    matchingCoolAccessTierStats.Size += blobSize;
                                    matchingCoolAccessTierStats.BlobNames.Add(blob.Name);
                                }
                                break;
                            case "Archive":
                                var archiveAccessTierStats = containerStats.BlobsStatistics[AccessTier.Archive];
                                archiveAccessTierStats.Count += 1;
                                archiveAccessTierStats.Size += blobSize;
                                if (doesBlobMatchFilterCriteria)
                                {
                                    var matchingArchiveAccessTierStats = containerStats.MatchingBlobsStatistics[AccessTier.Archive];
                                    matchingArchiveAccessTierStats.Count += 1;
                                    matchingArchiveAccessTierStats.Size += blobSize;
                                    matchingArchiveAccessTierStats.BlobNames.Add(blob.Name);
                                }
                                break;
                        }
                    }
                }
            }
            catch (Exception)
            {

            }
            return containerStats;
        }

        /// <summary>
        /// Changes the access tier of a block blob in a blob container.
        /// </summary>
        /// <param name="containerName">Name of the blob container.</param>
        /// <param name="blobName">Name of the blob.</param>
        /// <param name="targetTier"><see cref="AccessTier"/> which indicates the new access tier for the blob.</param>
        /// <returns>True or false.</returns>
        public static async Task<bool> ChangeAccessTier(string containerName, string blobName, AccessTier targetTier)
        {
            try
            {
                BlockBlobClient blob = s_blobServiceClient.GetBlobContainerClient(containerName).GetBlockBlobClient(blobName);
                await blob.SetAccessTierAsync(targetTier);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static string StorageAccountName
        {
            get
            {
                return s_blobServiceClient.AccountName;
            }
        }

        /// <summary>
        /// Checks the blob to see if it can be archived.
        /// </summary>
        /// <param name="blob"></param>
        /// <param name="filterCriteria"></param>
        /// <returns></returns>
        private static async Task<bool> DoesBlobMatchFilterCriteriaAsync(BlobClient blob, Models.FilterCriteria filterCriteria)
        {
            BlobProperties blobProperties = await blob.GetPropertiesAsync();
            if (blobProperties.AccessTier == AccessTier.Archive) return false;
            var dateTimeFrom = filterCriteria.LastModifiedDateFrom ?? DateTime.MinValue;
            var dateTimeTo = filterCriteria.LastModifiedDateTo ?? DateTime.MaxValue;
            var minBlobSize = filterCriteria.MinBlobSize;
            bool isDateTimeCheckPassed = false;
            if (blobProperties.LastModified != default)
            {
                var lastModified = blobProperties.LastModified.DateTime;
                if (dateTimeFrom <= lastModified && dateTimeTo >= lastModified)
                {
                    isDateTimeCheckPassed = true;
                }
            }
            bool isBlobSizeCheckPassed = blobProperties.ContentLength >= minBlobSize;
            return isDateTimeCheckPassed || isBlobSizeCheckPassed;
        }
    }
}
