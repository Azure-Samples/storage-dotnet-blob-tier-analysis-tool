using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BlobTierAnalysisTool.Models
{
    public class BlobsStatistics
    {
        private readonly List<string> _blobNames;

        private double _storageCostPerGbPerMonth;

        public BlobsStatistics(double storageCostPerGbPerMonth)
        {
            _blobNames = new List<string>();
            _storageCostPerGbPerMonth = storageCostPerGbPerMonth;
        }

        /// <summary>
        /// Gets or sets the number of blobs.
        /// </summary>
        public long Count { get; set; }

        /// <summary>
        /// Gets or sets the total size of blobs in bytes.
        /// </summary>
        public long Size { get; set; }

        /// <summary>
        /// Gets the storage cost / GB / month.
        /// </summary>
        public double StorageCostPerGbPerMonth
        {
            get { return _storageCostPerGbPerMonth; }
        }

        /// <summary>
        /// Gets a list of blob names.
        /// </summary>
        public List<string> BlobNames
        {
            get { return _blobNames; }
        }
    }
}
