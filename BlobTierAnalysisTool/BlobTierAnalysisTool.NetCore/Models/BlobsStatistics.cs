using Microsoft.WindowsAzure.Storage.Blob;
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

        public BlobsStatistics()
        {
            _blobNames = new List<string>();
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
        /// Gets a list of blob names.
        /// </summary>
        public List<string> BlobNames
        {
            get { return _blobNames; }
        }
    }
}
