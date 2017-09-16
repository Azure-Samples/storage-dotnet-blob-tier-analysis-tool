using System.Collections.Generic;

namespace BlobTierAnalysisTool.Models
{
    public class FilesStatistics
    {
        private readonly List<string> _fileNames;

        public FilesStatistics()
        {
            _fileNames = new List<string>();
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
        public List<string> FileNames
        {
            get { return _fileNames; }
        }
    }
}
