using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Microsoft.WindowsAzure.Storage;

namespace BlobTierAnalysisTool
{
    class Program
    {
        private const string SourceTypeArgumentName = "/SourceType";
        private const string ConnectionStringArgumentName = "/ConnectionString";
        private const string SourceArgumentName = "/Source";
        private const string DaysArgumentName = "/Days";
        private const string SizeArgumentName = "/Size";
        private static Dictionary<StandardBlobTier, Models.StorageCosts> storageCosts = null;
        static void Main(string[] args)
        {
            Console.WriteLine("*********************************************************************************");
            Console.WriteLine("Welcome to the Blob Tier Ananlysis Tool. This tool can do two things for you:");
            Console.WriteLine("1. It can analyze the contents of a local folder/file share and give you an");
            Console.WriteLine("   estimate of storage costs by various access tiers (Hot, Cool, Archive) should");
            Console.WriteLine("   you decide to store the files in Azure Storage");
            Console.WriteLine("2. It can analyze one or more blob containers in your storage account and will");
            Console.WriteLine("   give you an estimate of storage costs under different access tiers. If you like,");
            Console.WriteLine("   this tool will also move blobs across different access tiers for you.");
            Console.WriteLine();
            Console.WriteLine("For number 2, Please note that this tool will only work with:");
            Console.WriteLine("1) \"Blob Storage\" accounts with \"LRS\" redundancy in \"US East 2\" region.");
            Console.WriteLine("2) Subscriptions enable for arhiving.");
            Console.WriteLine();
            Console.WriteLine("Please read https://azure.microsoft.com/en-us/blog/announcing-the-public-preview-of-azure-archive-blob-storage-and-blob-level-tiering/ for more details");
            Console.WriteLine("*********************************************************************************");
            Console.WriteLine();
            storageCosts = new Dictionary<StandardBlobTier, Models.StorageCosts>()
            {
                { StandardBlobTier.Hot, new Models.StorageCosts(0.0184, 0.05, 0.004, 0, 0) },
                { StandardBlobTier.Cool, new Models.StorageCosts(0.01, 0.10, 0.01, 0.01, 0.0025) },
                { StandardBlobTier.Archive, new Models.StorageCosts(0.0018, 0.30, 0.15, 0.0015, 0) }
            };
            IEnumerable<string> sourcesToScan = null;
            var sourceType = GetSourceTypeInput();
            if (sourceType == "L")
            {
                var folderToSearch = GetLocalSourceInput();
                sourcesToScan = new List<string>()
                {
                    {folderToSearch}
                };
            }
            else
            {
                var connectionString = GetConnectionStringInput();
                var containerToSearch = GetContainerInput();
                if (containerToSearch == "*")
                {
                    Console.WriteLine($"Listing blob containers in \"{Helpers.BlobStorageHelper.StorageAccount.Credentials.AccountName}\" storage account. Please wait.");
                    sourcesToScan = Helpers.BlobStorageHelper.ListContainers().GetAwaiter().GetResult();
                }
                else
                {
                    Console.WriteLine($"Checking if \"{containerToSearch}\" blob container exists in the storage account. Please wait.");
                    var doesContainerExists = Helpers.BlobStorageHelper.DoesContainerExists(containerToSearch).GetAwaiter().GetResult();
                    if (!doesContainerExists)
                    {
                        Console.WriteLine("Specified blob container does not exist. Application will terminate now.");
                        Console.WriteLine("Press any key to terminate the application");
                        Console.ReadKey();
                        ExitApplicationIfRequired("X");
                    }
                    else
                    {
                        sourcesToScan = new List<string>()
                        {
                            {containerToSearch}
                        };
                    }
                }
            }

            var numDaysSinceLastModifiedFilterCriteria = GetBlobOrFileLastModifiedDateFilterCriteriaInput();
            var blobSizeFilterCriteria = GetBlobOrFileSizeFilterCriteriaInput();
            var filterCriteria = new Models.FilterCriteria()
            {
                MinBlobSize = blobSizeFilterCriteria,
                LastModifiedDateTo = DateTime.UtcNow.Date.AddDays(0 - numDaysSinceLastModifiedFilterCriteria)
            };
            if (sourceType == "L")
            {
                AnalyzeLocalStorage(sourcesToScan, filterCriteria);
            }
            else
            {
                AnalyzeStorageAccount(sourcesToScan, filterCriteria);
            }

            Console.WriteLine("Press any key to terminate the application.");
            Console.ReadLine();
        }

        private static void AnalyzeLocalStorage(IEnumerable<string> folderNames, Models.FilterCriteria filterCriteria)
        {
            List<Models.FolderStatistics> foldersStatistics = new List<Models.FolderStatistics>();
            Models.FolderStatistics summaryFoldersStatistics = new Models.FolderStatistics("Summary");
            var text = string.Empty;
            foreach (var folderName in folderNames)
            {
                Console.WriteLine($"Analyzing files in \"{folderName}\" folder. Please wait.");
                var folderStatistics = Helpers.FileSystemHelper.AnalyzeFolder(folderName, filterCriteria);
                summaryFoldersStatistics.FilesStatistics.Count += folderStatistics.FilesStatistics.Count;
                summaryFoldersStatistics.FilesStatistics.Size += folderStatistics.FilesStatistics.Size;
                summaryFoldersStatistics.MatchingFilesStatistics.Count += folderStatistics.MatchingFilesStatistics.Count;
                summaryFoldersStatistics.MatchingFilesStatistics.Size += folderStatistics.MatchingFilesStatistics.Size;
                text = string.Format("{0, 40}{1, 40}", "Total Files Count/Size", "Matching Files Count/Size");
                Console.WriteLine(new string('-', text.Length));
                Console.WriteLine(text);
                Console.WriteLine(new string('-', text.Length));
                var filesSizeCountString = $"{folderStatistics.FilesStatistics.Count}/{Helpers.Utils.SizeAsString(folderStatistics.FilesStatistics.Size)} ({folderStatistics.FilesStatistics.Size} bytes)";
                var matchingFilesSizeCountString = $"{folderStatistics.MatchingFilesStatistics.Count}/{Helpers.Utils.SizeAsString(folderStatistics.MatchingFilesStatistics.Size)} ({folderStatistics.MatchingFilesStatistics.Size} bytes)";
                text = string.Format("{0, 40}{1, 40}", filesSizeCountString, matchingFilesSizeCountString);
                Console.WriteLine(text);
                Console.WriteLine(new string('-', text.Length));
            }

            Console.WriteLine();
            Console.WriteLine("Summary");
            text = string.Format("{0, 40}{1, 40}", "Total Files Count/Size", "Matching Files Count/Size");
            var filesSizeCountSummaryString = $"{summaryFoldersStatistics.FilesStatistics.Count}/{Helpers.Utils.SizeAsString(summaryFoldersStatistics.FilesStatistics.Size)} ({summaryFoldersStatistics.FilesStatistics.Size} bytes)";
            var matchingFilesSizeSummaryCountString = $"{summaryFoldersStatistics.MatchingFilesStatistics.Count}/{Helpers.Utils.SizeAsString(summaryFoldersStatistics.MatchingFilesStatistics.Size)} ({summaryFoldersStatistics.MatchingFilesStatistics.Size} bytes)";
            text = string.Format("{0, 40}{1, 40}", filesSizeCountSummaryString, matchingFilesSizeSummaryCountString);
            Console.WriteLine(text);
            Console.WriteLine(new string('-', text.Length));
            Console.WriteLine();
            Console.WriteLine("Cost Estimator");
            Console.WriteLine("Scenario 1: Upload these files to Azure Storage and keep all blobs in \"Hot\" access tier");
            var storageCostsHotAccessTier = storageCosts[StandardBlobTier.Hot];
            var storageCostsCoolAccessTier = storageCosts[StandardBlobTier.Cool];
            var storageCostsArchiveAccessTier = storageCosts[StandardBlobTier.Archive];
            var totalFiles = summaryFoldersStatistics.MatchingFilesStatistics.Count;
            var totalSize = summaryFoldersStatistics.MatchingFilesStatistics.Size;
            var writeTransactionCost = storageCostsHotAccessTier.WriteOperationsCostPerTenThousand * totalFiles / 10000;
            var storageCost = storageCostsHotAccessTier.DataStorageCostPerGB * totalSize / Helpers.Constants.GB;
            Console.WriteLine(new string('-', 95));
            Console.WriteLine("{0, 70}{1, 20}", "One time cost of uploading files in Azure Storage:", writeTransactionCost.ToString("C"));
            Console.WriteLine("{0, 70}{1, 20}", "Storage costs/month:", storageCost.ToString("C"));
            Console.WriteLine(new string('-', 95));
            Console.WriteLine("{0, 70}{1, 20}", "Total:", (writeTransactionCost+storageCost).ToString("C"));
            Console.WriteLine(new string('-', 95));
            Console.WriteLine();
            Console.WriteLine("Scenario 2: Upload these files to Azure Storage and keep all blobs in \"Cool\" access tier");
            writeTransactionCost = storageCostsHotAccessTier.WriteOperationsCostPerTenThousand * totalFiles / 10000;
            storageCost = storageCostsCoolAccessTier.DataStorageCostPerGB * totalSize / Helpers.Constants.GB;
            var dataTierChangeCost = storageCostsCoolAccessTier.WriteOperationsCostPerTenThousand * totalFiles / 10000;
            Console.WriteLine(new string('-', 95));
            Console.WriteLine("{0, 70}{1, 20}", "One time cost of uploading files in Azure Storage:", writeTransactionCost.ToString("C"));
            Console.WriteLine("{0, 70}{1, 20}", "One time cost of changing access tier from \"Hot\" to \"Cool\":", dataTierChangeCost.ToString("C"));
            Console.WriteLine("{0, 70}{1, 20}", "Storage costs/month:", storageCost.ToString("C"));
            Console.WriteLine(new string('-', 95));
            Console.WriteLine("{0, 70}{1, 20}", "Total:", (writeTransactionCost + dataTierChangeCost + storageCost).ToString("C"));
            Console.WriteLine(new string('-', 95));
            Console.WriteLine();
            Console.WriteLine("Scenario 3: Upload these files to Azure Storage and keep all blobs in \"Archive\" access tier");
            writeTransactionCost = storageCostsHotAccessTier.WriteOperationsCostPerTenThousand * totalFiles / 10000;
            storageCost = storageCostsArchiveAccessTier.DataStorageCostPerGB * totalSize / Helpers.Constants.GB;
            dataTierChangeCost = storageCostsArchiveAccessTier.WriteOperationsCostPerTenThousand * totalFiles / 10000;
            Console.WriteLine(new string('-', 95));
            Console.WriteLine("{0, 70}{1, 20}", "One time cost of uploading files in Azure Storage:", writeTransactionCost.ToString("C"));
            Console.WriteLine("{0, 70}{1, 20}", "One time cost of changing access tier from \"Hot\" to \"Archive\":", dataTierChangeCost.ToString("C"));
            Console.WriteLine("{0, 70}{1, 20}", "Storage costs/month:", storageCost.ToString("C"));
            Console.WriteLine(new string('-', 95));
            Console.WriteLine("{0, 70}{1, 20}", "Total:", (writeTransactionCost + dataTierChangeCost + storageCost).ToString("C"));
            Console.WriteLine(new string('-', 95));
            Console.WriteLine();

        }

        private static void AnalyzeStorageAccount(IEnumerable<string> containerNames, Models.FilterCriteria filterCriteria)
        {
            long totalMatchingBlobs = 0;
            long totalMatchingBlobsSize = 0;
            double totalCostSavings = 0.0;
            var containersStats = new List<Models.ContainerStatistics>();
            var summaryContainerStats = new Models.ContainerStatistics("Summary");
            foreach (var containerName in containerNames)
            {
                Console.WriteLine($"Analyzing blobs in \"{containerName}\" blob container. Please wait.");
                var containerStats = Helpers.BlobStorageHelper.AnalyzeContainer(containerName, filterCriteria).GetAwaiter().GetResult();
                containersStats.Add(containerStats);
                var text = string.Format("{0, 12}{1, 40}{2, 40}", "Access Tier", "Total Blobs Count/Size", "Matching Blobs Count/Size");
                Console.WriteLine(new string('-', text.Length));
                Console.WriteLine(text);
                Console.WriteLine(new string('-', text.Length));
                foreach (var key in containerStats.BlobsStatistics.Keys)
                {
                    var label = key.ToString();
                    var blobStatistics = containerStats.BlobsStatistics[key];
                    var matchingBlobStatistics = containerStats.MatchingBlobsStatistics[key];
                    var blobSummaryStatistics = summaryContainerStats.BlobsStatistics[key];
                    blobSummaryStatistics.Count += blobStatistics.Count;
                    blobSummaryStatistics.Size += blobStatistics.Size;
                    var matchingSummaryStatistics = summaryContainerStats.MatchingBlobsStatistics[key];
                    matchingSummaryStatistics.Count += matchingBlobStatistics.Count;
                    matchingSummaryStatistics.Size += matchingBlobStatistics.Size;
                    totalMatchingBlobs += matchingBlobStatistics.Count;
                    totalMatchingBlobsSize += matchingBlobStatistics.Size;
                    var blobsSizeCountString = $"{blobStatistics.Count}/{Helpers.Utils.SizeAsString(blobStatistics.Size)} ({blobStatistics.Size} bytes)";
                    var matchingBlobsSizeCountString = $"{matchingBlobStatistics.Count}/{Helpers.Utils.SizeAsString(matchingBlobStatistics.Size)} ({matchingBlobStatistics.Size} bytes)";
                    //var storageCostCurrent = (matchingBlobStatistics.StorageCostPerGbPerMonth * matchingBlobStatistics.Size) / Helpers.Constants.GB;
                    //var storageCostAfterArchiving = (containerStats.BlobsStatistics[StandardBlobTier.Archive].StorageCostPerGbPerMonth * matchingBlobStatistics.Size) / Helpers.Constants.GB;
                    //var costSavings = storageCostCurrent - storageCostAfterArchiving;
                    //totalCostSavings += costSavings;
                    //var percentCostSavings = costSavings > 0 ? (((costSavings - storageCostAfterArchiving) / costSavings)).ToString("P") : "N/A";
                    //var costSavingsString = $"{costSavings.ToString("C")} ({percentCostSavings})";
                    text = string.Format("{0, 12}{1, 40}{2, 40}", label, blobsSizeCountString, matchingBlobsSizeCountString);
                    Console.WriteLine(text);
                }
                Console.WriteLine(new string('-', text.Length));
            }
            Console.WriteLine();
            if (containersStats.Count > 1)
            {
                Console.WriteLine("Summary for all containers");
                var text = string.Format("{0, 12}{1, 40}{2, 40}", "Access Tier", "Total Blobs Count/Size", "Matching Blobs Count/Size");
                Console.WriteLine(new string('-', text.Length));
                Console.WriteLine(text);
                Console.WriteLine(new string('-', text.Length));
                foreach (var key in summaryContainerStats.BlobsStatistics.Keys)
                {
                    var label = key.ToString();
                    var blobStatistics = summaryContainerStats.BlobsStatistics[key];
                    var matchingBlobStatistics = summaryContainerStats.MatchingBlobsStatistics[key];
                    var blobsSizeCountString = $"{blobStatistics.Count}/{Helpers.Utils.SizeAsString(blobStatistics.Size)} ({blobStatistics.Size} bytes)";
                    var matchingBlobsSizeCountString = $"{matchingBlobStatistics.Count}/{Helpers.Utils.SizeAsString(matchingBlobStatistics.Size)} ({matchingBlobStatistics.Size} bytes)";
                    text = string.Format("{0, 12}{1, 40}{2, 40}", label, blobsSizeCountString, matchingBlobsSizeCountString);
                    Console.WriteLine(text);
                }
                Console.WriteLine(new string('-', text.Length));
            }

            if (totalMatchingBlobs > 0)
            {
                Console.WriteLine($"You could potentially save {totalCostSavings.ToString("c")} in monthly storage costs by moving {totalMatchingBlobs} blobs to the Archive tier. Please press \"Y\" to continue.");
                var input = Console.ReadKey();
                if (input.Key.ToString().ToUpperInvariant() == "Y")
                    Console.WriteLine();
                {
                    foreach (var item in containersStats)
                    {
                        foreach (var key in item.MatchingBlobsStatistics.Keys)
                        {
                            var blobNames = item.MatchingBlobsStatistics[key].BlobNames;
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
        }

        /// <summary>
        /// Reads the source type from command line arguments or 
        /// via user input.
        /// </summary>
        /// <returns>Source type.</returns>
        /// </summary>
        private static string GetSourceTypeInput()
        {
            var sourceTypeInput = TryParseCommandLineArgumentsToExtractValue(SourceTypeArgumentName);
            var sourceType = string.Empty;
            if (!string.IsNullOrWhiteSpace(sourceTypeInput))
            {
                sourceType = sourceTypeInput.Remove(0, SourceTypeArgumentName.Length + 1);
            }
            sourceType = sourceType.ToUpperInvariant();
            if (sourceType == "C" || sourceType == "L")
            {
                return sourceType;
            }
            else
            {
                Console.WriteLine(new string('*', 30));
                Console.WriteLine("Please enter the source type you want to analyze.");
                Console.WriteLine("Valid values are [L]ocal or [C]loud.");
                Console.WriteLine("Please note that you can also specify source type as command line argument as well.");
                Console.WriteLine("Simply specify /SourceType:<L|C> in command line.");
                Console.WriteLine(new string('*', 30));
                sourceType = Console.ReadLine().ToUpperInvariant();
                ExitApplicationIfRequired(sourceType);
                if (!string.IsNullOrWhiteSpace(sourceType) && (sourceType == "C" || sourceType == "L"))
                {
                    return sourceType;
                }
                else
                {
                    Console.WriteLine("Invalid input. Please specify either \"L\" or \"C\" as source type");
                    return GetSourceTypeInput();
                }
            }
        }

        /// <summary>
        /// Reads the local folder path from command line arguments or
        /// via user input.
        /// </summary>
        /// <returns>Folder path</returns>
        private static string GetLocalSourceInput()
        {
            var folderPathArgument = TryParseCommandLineArgumentsToExtractValue(SourceArgumentName);
            var folderName = string.Empty;
            if (!string.IsNullOrWhiteSpace(folderPathArgument))
            {
                folderName = folderPathArgument.Remove(0, SourceArgumentName.Length + 1);
            }
            if (!string.IsNullOrWhiteSpace(folderName) && Directory.Exists(folderName))
            {
                return folderName;
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine(new string('*', 30));
                Console.WriteLine("Enter the full path of a directory that you want to analyze.");
                Console.WriteLine("Please note that you can also specify folder path as command line argument as well.");
                Console.WriteLine("Simply specify /Source:<fullpath> in command line.");
                Console.WriteLine(new string('*', 30));
                Console.WriteLine();
                folderName = Console.ReadLine().Trim().ToLowerInvariant();
                ExitApplicationIfRequired(folderName);
                if (!string.IsNullOrWhiteSpace(folderName) && Directory.Exists(folderName))
                {
                    return folderName;
                }
                else
                {
                    Console.WriteLine("Invalid folder path defined. Please try again.");
                    return GetLocalSourceInput();
                }
            }
        }

        /// <summary>
        /// Reads the storage account connection string from command line arguments or 
        /// via user input.
        /// </summary>
        /// <returns>Storage account connection string.</returns>
        private static string GetConnectionStringInput()
        {
            var connectionStringArgument = TryParseCommandLineArgumentsToExtractValue(ConnectionStringArgumentName);
            var connectionString = string.Empty;
            if (!string.IsNullOrWhiteSpace(connectionStringArgument))
            {
                connectionString = connectionStringArgument.Remove(0, ConnectionStringArgumentName.Length + 1);
            }
            if (!string.IsNullOrWhiteSpace(connectionString) && Helpers.BlobStorageHelper.ParseConnectionString(connectionString))
            {
                return connectionString;
            }
            else
            {
                Console.WriteLine(new string('*', 30));
                Console.WriteLine("Please enter connection string for your storage account.");
                Console.WriteLine("Connection string should be in the following format:");
                Console.WriteLine("DefaultEndpointsProtocol=https;AccountName=<youraccountname>;AccountKey=<youraccountkey>");
                Console.WriteLine("Please note that you can also specify connection string as command line argument as well.");
                Console.WriteLine("Simply specify /ConnectionString:<yourconnectionstring> in command line.");
                Console.WriteLine(new string('*', 30));
                connectionString = Console.ReadLine();
                ExitApplicationIfRequired(connectionString);
                if (!string.IsNullOrWhiteSpace(connectionString) && Helpers.BlobStorageHelper.ParseConnectionString(connectionString))
                {
                    return connectionString;
                }
                else
                {
                    Console.WriteLine("Invalid connection string specified. Please try again.");
                    return GetConnectionStringInput();
                }
            }
        }

        /// <summary>
        /// Reads the container name from command line from command line arguments or
        /// via user input
        /// </summary>
        /// <returns>Blob container name.</returns>
        private static string GetContainerInput()
        {
            var containerArgument = TryParseCommandLineArgumentsToExtractValue(SourceArgumentName);
            var containerName = string.Empty;
            if (!string.IsNullOrWhiteSpace(containerArgument))
            {
                containerName = containerArgument.Remove(0, SourceArgumentName.Length + 1);
            }
            if (!string.IsNullOrWhiteSpace(containerName))
            {
                containerName = containerName.Trim().ToLowerInvariant();
            }
            if (Helpers.Utils.IsValidContainerName(containerName))
            {
                return containerName;
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine(new string('*', 30));
                Console.WriteLine("Enter the blob container name that you want to analyze. To analyze all blob containers, please enter *");
                Console.WriteLine("Please note that you can also specify container name as command line argument as well.");
                Console.WriteLine("Simply specify /Source:<blobcontainername> in command line.");
                Console.WriteLine(new string('*', 30));
                Console.WriteLine();
                containerName = Console.ReadLine().Trim().ToLowerInvariant();
                ExitApplicationIfRequired(containerName);
                if (!string.IsNullOrWhiteSpace(containerName) && Helpers.Utils.IsValidContainerName(containerName))
                {
                    return containerName;
                }
                else
                {
                    Console.WriteLine("Invalid container name. Please try again.");
                    return GetContainerInput();
                }
            }
        }

        /// <summary>
        /// Reads the number of days since a blob/file has been last modified from command line arguments or
        /// via user input. This is used for filtering blobs/files.
        /// </summary>
        /// <returns>Number of days since a blob/file has been last modified.</returns>
        private static int GetBlobOrFileLastModifiedDateFilterCriteriaInput()
        {
            var numDaysArgument = TryParseCommandLineArgumentsToExtractValue(DaysArgumentName);
            int numDays = 0;
            if (!string.IsNullOrWhiteSpace(numDaysArgument))
            {
                var daysValue = numDaysArgument.Remove(0, DaysArgumentName.Length + 1);
                if (Int32.TryParse(daysValue, out numDays))
                {
                    if (numDays > 0) return numDays;
                }
            }
            Console.WriteLine();
            Console.WriteLine(new string('*', 30));
            Console.WriteLine("Enter the number of days before which a blob/file was last modified to be considered for analysis. Press \"Enter\" key for default value (30 days).");
            Console.WriteLine("Please note that you can also specify this value as command line argument as well.");
            Console.WriteLine("Simply specify /Days:<numberofdays> in command line.");
            Console.WriteLine(new string('*', 30));
            Console.WriteLine();
            var consoleInput = Console.ReadLine().Trim().ToLowerInvariant();
            ExitApplicationIfRequired(consoleInput);
            if (!Int32.TryParse(consoleInput, out numDays) || numDays <= 0)
            {
                Console.WriteLine("Invalid input. Please try again.");
                return GetBlobOrFileLastModifiedDateFilterCriteriaInput();
            }
            return numDays;
        }

        /// <summary>
        /// Reads the minimum size of a blob/file from command line arguments or
        /// via user input. This is used for filtering blobs/files.
        /// </summary>
        /// <returns>Minimum size of a blob/file.</returns>
        private static long GetBlobOrFileSizeFilterCriteriaInput()
        {
            var sizeArgument = TryParseCommandLineArgumentsToExtractValue(SizeArgumentName);
            long size = 0;
            if (!string.IsNullOrWhiteSpace(sizeArgument))
            {
                var sizeValue = sizeArgument.Remove(0, SizeArgumentName.Length + 1);
                if (long.TryParse(sizeValue, out size))
                {
                    if (size >= 0) return size;
                }
            }
            Console.WriteLine();
            Console.WriteLine(new string('*', 30));
            Console.WriteLine("Enter the minimum size of the blob/file in bytes to be considered for analysis. Press \"Enter\" key for default value (0 bytes).");
            Console.WriteLine("Please note that you can also specify this value as command line argument as well.");
            Console.WriteLine("Simply specify /Size:<minimumsizeinbytes> in command line.");
            Console.WriteLine(new string('*', 30));
            Console.WriteLine();
            var consoleInput = Console.ReadLine().Trim().ToLowerInvariant();
            ExitApplicationIfRequired(consoleInput);
            if (consoleInput == "")
            {
                return 0;
            }
            if (!long.TryParse(consoleInput, out size) || size < 0)
            {
                Console.WriteLine("Invalid input. Please try again.");
                return GetBlobOrFileSizeFilterCriteriaInput();
            }
            return size;
        }

        /// <summary>
        /// Exits the application if <paramref name="consoleInput"/> value equals "X" or "x"
        /// </summary>
        /// <param name="consoleInput">
        /// Console input entered by user.
        /// </param>
        private static void ExitApplicationIfRequired(string consoleInput)
        {
            if (!string.IsNullOrWhiteSpace(consoleInput) && (consoleInput.ToUpperInvariant() == "X"))
            {
                Environment.Exit(0);
            }
        }

        /// <summary>
        /// Helper method to parse command line arguments to find a matching command line argument.
        /// </summary>
        /// <param name="argumentToSearch">
        /// Command line argument to search.
        /// </param>
        /// <returns>
        /// Either matching command line argument or null.
        /// </returns>
        private static string TryParseCommandLineArgumentsToExtractValue(string argumentToSearch)
        {
            var arguments = Environment.GetCommandLineArgs();
            return arguments.FirstOrDefault(a => a.StartsWith(argumentToSearch));
        }
    }
}
