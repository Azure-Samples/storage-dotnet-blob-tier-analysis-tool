namespace BlobTierAnalysisTool.Models
{
    public class FolderStatistics
    {
        private readonly FilesStatistics _filesStatistics, _matchingFilesStatistics;

        private readonly string _name;

        public FolderStatistics(string folderName)
        {
            _name = folderName;
            _filesStatistics = new FilesStatistics();
            _matchingFilesStatistics = new FilesStatistics();
        }

        public string Name
        {
            get
            {
                return _name;
            }
        }

        public FilesStatistics FilesStatistics
        {
            get
            {
                return _filesStatistics;
            }
        }

        public FilesStatistics MatchingFilesStatistics
        {
            get
            {
                return _matchingFilesStatistics;
            }
        }
    }
}
