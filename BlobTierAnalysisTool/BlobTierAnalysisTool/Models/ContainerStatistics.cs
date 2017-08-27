using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Blob;

namespace BlobTierAnalysisTool.Models
{
    public class ContainerStatistics
    {
        private readonly Dictionary<StandardBlobTier, BlobsStatistics> _blobsStatistics, _archivableBlobsStatistics;

        private readonly string _name;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="blobContainerName">Name of the blob container.</param>
        public ContainerStatistics(string blobContainerName)
        {
            _name = blobContainerName;
            _blobsStatistics = new Dictionary<StandardBlobTier, BlobsStatistics>()
            {
                {StandardBlobTier.Hot, new BlobsStatistics(0.0184)},
                {StandardBlobTier.Cool, new BlobsStatistics(0.01)},
                {StandardBlobTier.Archive, new BlobsStatistics(0.0018)}
            };
            _archivableBlobsStatistics = new Dictionary<StandardBlobTier, BlobsStatistics>()
            {
                {StandardBlobTier.Hot, new BlobsStatistics(0.0184)},
                {StandardBlobTier.Cool, new BlobsStatistics(0.01)},
                {StandardBlobTier.Archive, new BlobsStatistics(0.0018)}
            };
        }

        /// <summary>
        /// Gets the container name.
        /// </summary>
        public string Name
        {
            get
            {
                return _name;
            }
        }

        /// <summary>
        /// Gets the blob statistics for the container.
        /// </summary>
        public Dictionary<StandardBlobTier, BlobsStatistics> BlobsStatistics
        {
            get
            {
                return _blobsStatistics;
            }
        }

        /// <summary>
        /// Gets the blob statistics for the container.
        /// </summary>
        public Dictionary<StandardBlobTier, BlobsStatistics> ArchivableBlobsStatistics
        {
            get
            {
                return _archivableBlobsStatistics;
            }
        }

    }
}
