using Azure.Storage.Blobs.Models;
using System.Collections.Generic;

namespace BlobTierAnalysisTool.Models
{
    public class ContainerStatistics
    {
        private readonly Dictionary<AccessTier, BlobsStatistics> _blobsStatistics, _matchingBlobsStatistics;

        private readonly string _name;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="blobContainerName">Name of the blob container.</param>
        public ContainerStatistics(string blobContainerName)
        {
            _name = blobContainerName;
            _blobsStatistics = new Dictionary<AccessTier, BlobsStatistics>()
            {
                {AccessTier.Hot, new BlobsStatistics()},
                {AccessTier.Cool, new BlobsStatistics()},
                {AccessTier.Archive, new BlobsStatistics()}
            };
            _matchingBlobsStatistics = new Dictionary<AccessTier, BlobsStatistics>()
            {
                {AccessTier.Hot, new BlobsStatistics()},
                {AccessTier.Cool, new BlobsStatistics()},
                {AccessTier.Archive, new BlobsStatistics()}
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
        public Dictionary<AccessTier, BlobsStatistics> BlobsStatistics
        {
            get
            {
                return _blobsStatistics;
            }
        }

        /// <summary>
        /// Gets the blob statistics for the container.
        /// </summary>
        public Dictionary<AccessTier, BlobsStatistics> MatchingBlobsStatistics
        {
            get
            {
                return _matchingBlobsStatistics;
            }
        }

    }
}
