using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace BlobTierAnalysisTool.Helpers
{
    public static class FileSystemHelper
    {
        public static Models.FolderStatistics AnalyzeFolder(string folderName, Models.FilterCriteria filterCriteria)
        {
            Models.FolderStatistics folderStatistics = new Models.FolderStatistics(folderName);
            try
            {
                var files = ListFiles(folderName);
                var filesCount = files.LongCount();
                var totalFilesSize = files.Sum(f => f.Length);
                var lastModifiedDateFrom = filterCriteria.LastModifiedDateFrom ?? DateTime.MinValue;
                var lastModifiedDateTo = filterCriteria.LastModifiedDateTo ?? DateTime.MaxValue;
                var filteredFiles = files.Where(f => f.Length >= filterCriteria.MinBlobSize && f.LastWriteTime >= lastModifiedDateFrom && f.LastWriteTime <= lastModifiedDateTo);
                var filteredFilesCount = filteredFiles.LongCount();
                var filteredFilesSize = filteredFiles.Sum(f => f.Length);
                folderStatistics.FilesStatistics.Count = filesCount;
                folderStatistics.FilesStatistics.Size = totalFilesSize;
                folderStatistics.MatchingFilesStatistics.Count = filteredFilesCount;
                folderStatistics.MatchingFilesStatistics.Size = filteredFilesSize;
                return folderStatistics;
            }
            catch (Exception)
            {
                return folderStatistics;
            }
        }

        private static IEnumerable<FileInfo> ListFiles(string folderName)
        {
            List<FileInfo> files = new List<FileInfo>();
            try
            {
                var directory = new DirectoryInfo(folderName);
                var foldersInDirectory = directory.GetDirectories("*.*", SearchOption.TopDirectoryOnly);
                foreach (var folderInDirectory in foldersInDirectory)
                {
                    var filesList = ListFiles(folderInDirectory.FullName);
                    files.AddRange(filesList);
                }
                var filesInDirectory = directory.GetFiles("*.*", SearchOption.TopDirectoryOnly);
                files.AddRange(filesInDirectory.ToList());
                return files;
            }
            catch (Exception excep)
            {
                return files;
            }
        }
    }
}
