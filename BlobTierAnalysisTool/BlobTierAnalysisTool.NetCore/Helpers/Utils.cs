using System;
using System.Text.RegularExpressions;

namespace BlobTierAnalysisTool.Helpers
{
    public static class Utils
    {
        public static string SizeAsString(long size)
        {
            var units = "Bytes";
            var sizeString = size.ToString();
            if (size < Constants.MB)
            {
                sizeString = (((double)size) / Constants.KB).ToString("F2");
                units = "KB";
            }
            else if (size < Constants.GB)
            {
                sizeString = (((double)size) / Constants.MB).ToString("F2");
                units = "MB";
            }
            else if (size < Constants.TB)
            {
                sizeString = (((double)size) / Constants.GB).ToString("F2");
                units = "GB";
            }
            else if (size < Constants.PB)
            {
                sizeString = (((double)size) / Constants.TB).ToString("F2");
                units = "TB";
            }
            else
            {
                sizeString = (((double)size) / Constants.PB).ToString("F2");
                units = "PB";
            }
            return $"{sizeString} {units}";
        }

        public static bool IsValidContainerName(string containerName)
        {
            if (containerName == "*")
            {
                return true;
            }
            try
            {
                Microsoft.WindowsAzure.Storage.NameValidator.ValidateContainerName(containerName);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }
}
