using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BlobTierAnalysisTool
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("*********************************************************************************");
            Console.WriteLine("Welcome to the Blob Tier Ananlysis Tool. This tool will analyze one or more");
            Console.WriteLine("blob containers in your storage account for potential storage cost savings if you");
            Console.WriteLine("move block blobs from the \"Hot\" or \"Cool\" tier to the \"Archive\" tier. It");
            Console.WriteLine("will also enable you to move analyzed blobs to the \"Archive\" tier.");
            Console.WriteLine("*********************************************************************************");
            Console.WriteLine();
            var containerToSearch = GetContainerInput();
            var numDaysSinceLastModifiedFilterCriteria = GetBlobLastModifiedDateFilterCriteriaInput();
            Console.WriteLine("----------------------------------------------------------------");
            var blobSizeFilterCriteria = GetBlobSizeFilterCriteriaInput();
            Console.WriteLine("----------------------------------------------------------------");
            IEnumerable<string> containerNames = null;
            if (containerToSearch == "" || containerToSearch == "*")
            {
                Console.WriteLine($"Listing blob containers in \"{Helpers.BlobStorageHelper.StorageAccount.Credentials.AccountName}\" storage account. Please wait.");
                containerNames = Helpers.BlobStorageHelper.ListContainers().GetAwaiter().GetResult();
                foreach (var item in containerNames)
                {
                    Console.WriteLine(item);
                }
                Console.WriteLine("----------------------------------------------------------------");
            }
            else
            {
                containerNames = new List<string>()
                {
                    {containerToSearch}
                };
            }
            var filterCriteria = new Models.FilterCriteria()
            {
                MinBlobSize = blobSizeFilterCriteria,
                LastModifiedDateTo = DateTime.UtcNow.Date.AddDays(0 - numDaysSinceLastModifiedFilterCriteria)
            };
            long totalArchivableBlobs = 0;
            long totalArchivableBlobsSize = 0;
            double totalCostSavings = 0.0;
            var containersStats = new List<Models.ContainerStatistics>();
            foreach (var containerName in containerNames)
            {
                Console.WriteLine($"Analyzing blobs in \"{containerName}\" blob container for archiving. Please wait.");
                var containerStats = Helpers.BlobStorageHelper.AnalyzeContainerForArchival(containerName, filterCriteria).GetAwaiter().GetResult();
                containersStats.Add(containerStats);
                var headerText = $"Analysis result for \"{containerName}\" blob container";
                Console.WriteLine(headerText);
                Console.WriteLine(new string('-', headerText.Length));
                var text = string.Format("{0, 12}{1, 40}{2, 40}{3, 20}", "Access Tier", "Total Blobs Count/Size", "Archivable Blobs Count/Size", "Cost Savings");
                Console.WriteLine(new string('-', text.Length));
                Console.WriteLine(text);
                Console.WriteLine(new string('-', text.Length));
                foreach (var key in containerStats.BlobsStatistics.Keys)
                {
                    var label = key.ToString();
                    var blobStatistics = containerStats.BlobsStatistics[key];
                    var archivableBlobStatistics = containerStats.ArchivableBlobsStatistics[key];
                    totalArchivableBlobs += archivableBlobStatistics.Count;
                    totalArchivableBlobsSize += archivableBlobStatistics.Size;
                    var blobsSizeCountString = $"{blobStatistics.Count}/{Helpers.Utils.SizeAsString(blobStatistics.Size)} ({blobStatistics.Size} bytes)";
                    var archivableBlobsSizeCountString = $"{archivableBlobStatistics.Count}/{Helpers.Utils.SizeAsString(archivableBlobStatistics.Size)} ({archivableBlobStatistics.Size} bytes)";
                    var storageCostCurrent = (archivableBlobStatistics.StorageCostPerGbPerMonth * archivableBlobStatistics.Size) / Helpers.Constants.GB;
                    var storageCostAfterArchiving = (containerStats.BlobsStatistics[StandardBlobTier.Archive].StorageCostPerGbPerMonth * archivableBlobStatistics.Size) / Helpers.Constants.GB;
                    var costSavings = storageCostCurrent - storageCostAfterArchiving;
                    totalCostSavings += costSavings;
                    var percentCostSavings = costSavings > 0 ? (((costSavings - storageCostAfterArchiving) / costSavings)).ToString("P") : "N/A";
                    var costSavingsString = $"{costSavings.ToString("C")} ({percentCostSavings})";
                    text = string.Format("{0, 12}{1, 40}{2, 40}{3, 20}", label, blobsSizeCountString, archivableBlobsSizeCountString, costSavingsString);
                    Console.WriteLine(text);
                }
                Console.WriteLine(new string('-', text.Length));
            }
            if (totalArchivableBlobs > 0)
            {
                Console.WriteLine($"You could potentially save {totalCostSavings.ToString("c")} in monthly storage costs by moving {totalArchivableBlobs} blobs to the Archive tier. Please press \"Y\" to continue.");
                var input = Console.ReadKey();
                if (input.Key.ToString().ToUpperInvariant() == "Y")
                    Console.WriteLine();
                {
                    foreach (var item in containersStats)
                    {
                        foreach (var key in item.ArchivableBlobsStatistics.Keys)
                        {
                            var blobNames = item.ArchivableBlobsStatistics[key].BlobNames;
                            foreach (var blobName in blobNames)
                            {
                                Console.WriteLine($"Archiving \"{blobName}\" in \"{item.Name}\" blob container.");
                                var result = Helpers.BlobStorageHelper.ChangeAccessTier(item.Name, blobName, StandardBlobTier.Archive).GetAwaiter().GetResult();
                                if (result)
                                {
                                    var messageText = $"\"{blobName}\" in \"{item.Name}\" blob container archived successfully.";
                                    Console.WriteLine(messageText);
                                    Console.WriteLine(new string('-', messageText.Length));
                                }
                            }
                        }
                    }
                }
            }

            Console.WriteLine("Press any key to terminate the application.");
            Console.ReadLine();
        }

        private static string GetContainerInput()
        {
            Console.WriteLine("Enter the blob container name that you want to analyze for archival cost savings. Press \"Enter\" key for default (all blob containers) or \"X\" to terminate the application.");
            var consoleInput = Console.ReadLine().Trim().ToLowerInvariant();
            if (consoleInput == "x") Environment.Exit(0);
            if (consoleInput == "" || consoleInput == "*")
            {
                return consoleInput;
            }
            var containerNameRegex = new Regex("^([a-z0-9]+(-[a-z0-9]+)*)$");
            if ((consoleInput.Length < 3 || consoleInput.Length > 63) || (!containerNameRegex.IsMatch(consoleInput)))
            {
                Console.WriteLine("Invalid container name. Please try again.");
                Console.WriteLine("-----------------------------------------");
                return GetContainerInput();
            }
            return consoleInput;
        }

        private static int GetBlobLastModifiedDateFilterCriteriaInput()
        {
            Console.WriteLine("Enter the number of days before which a blob was last modified to be considered for archiving. Press \"Enter\" key for default (30) or \"X\" to terminate the application.");
            var consoleInput = Console.ReadLine().Trim().ToLowerInvariant();
            if (consoleInput == "x") Environment.Exit(0);
            if (consoleInput == "")
            {
                Console.WriteLine("Only the blobs which were modified before 30 days from today's date will be considered for archiving.");
                return 30;
            }
            int numDays = 0;
            if (Int32.TryParse(consoleInput, out numDays))
            {
                if (numDays <= 0)
                {
                    Console.WriteLine("Invalid input. Please try again.");
                    return GetBlobLastModifiedDateFilterCriteriaInput();
                }
                Console.WriteLine($"Only the blobs which were modified before {numDays} day(s) from today's date will be considered for archiving.");
                return numDays;
            }
            else
            {
                Console.WriteLine("Invalid input. Please try again.");
                return GetBlobLastModifiedDateFilterCriteriaInput();
            }
        }

        private static long GetBlobSizeFilterCriteriaInput()
        {
            Console.WriteLine("Enter the minimum size of the blob in bytes to be considered for archiving. Press \"Enter\" key for default (0 bytes) or \"X\" to terminate the application.");
            var consoleInput = Console.ReadLine().Trim().ToLowerInvariant();
            if (consoleInput == "x") Environment.Exit(0);
            if (consoleInput == "")
            {
                Console.WriteLine("Blobs with any size will be considered for archiving.");
                return 0;
            }
            long blobSize = 0;
            if (long.TryParse(consoleInput, out blobSize))
            {
                if (blobSize < 0)
                {
                    Console.WriteLine("Invalid input. Please try again.");
                    return GetBlobSizeFilterCriteriaInput();
                }
                Console.WriteLine($"Blobs with size more than or equal to {blobSize} byte(s) will be considered for archiving.");
                return blobSize;
            }
            else
            {
                Console.WriteLine("Invalid input. Please try again.");
                return GetBlobSizeFilterCriteriaInput();
            }
        }
    }
}
