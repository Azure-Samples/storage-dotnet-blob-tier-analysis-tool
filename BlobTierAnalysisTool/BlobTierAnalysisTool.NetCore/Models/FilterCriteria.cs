using System;

namespace BlobTierAnalysisTool.Models
{
    public class FilterCriteria
    {
        private bool _ignoreBlobsInCoolTier = false;

        private DateTime? _lastModifiedDateFrom;

        private DateTime? _lastModifiedDateTo;

        private long _minBlobSize = 0;

        /// <summary>
        /// Gets or sets the flag indicating if blobs in "Cool" tier should be ignored.
        /// </summary>
        public bool IgnoreBlobsInCoolTier
        {
            get { return _ignoreBlobsInCoolTier; }
            set { _ignoreBlobsInCoolTier = value; }
        }

        /// <summary>
        /// Gets or sets the lower end of date/time range during which a blob has been modified.
        /// </summary>
        public DateTime? LastModifiedDateFrom
        {
            get { return _lastModifiedDateFrom; }
            set { _lastModifiedDateFrom = value; }
        }

        /// <summary>
        /// Gets or sets the higher end of date/time range during which a blob has been modified.
        /// </summary>
        public DateTime? LastModifiedDateTo
        {
            get { return _lastModifiedDateTo; }
            set { _lastModifiedDateTo = value; }
        }

        /// <summary>
        /// Gets or sets the minimum blob size.
        /// </summary>
        public long MinBlobSize
        {
            get { return _minBlobSize; }
            set { _minBlobSize = value; }
        }
    }
}
