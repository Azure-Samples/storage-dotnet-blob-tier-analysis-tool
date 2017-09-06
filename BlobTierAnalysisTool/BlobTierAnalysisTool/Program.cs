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
        private const string SourceTypeArgumentName = "/SourceType:";
        private const string ConnectionStringArgumentName = "/ConnectionString:";
        private const string SourceArgumentName = "/Source:";
        private const string DaysArgumentName = "/Days:";
        private const string SizeArgumentName = "/Size:";
        private static Dictionary<StandardBlobTier, Models.StorageCosts> storageCosts = null;
        private static IEnumerable<string> sourcesToScan = null;
        private static Models.FilterCriteria filterCriteria = null;
        static void Main(string[] args)
        {
            //double a = 100;
            //for (var i=0; i<a; i++)
            //{
            //    Console.Write("\r{0, 12}{1, 12}", (i / a).ToString("P"), ((a-i) / a).ToString("P"));
            //    //Console.Write((i / a).ToString("P"));
            //    System.Threading.Thread.Sleep(1000);
            //}
            Console.WriteLine("*********************************************************************************");
            Console.WriteLine("Welcome to the Blob Tier Ananlysis Tool. This tool can:");
            Console.WriteLine("1. Analyze the contents of a local folder/file share and give you an");
            Console.WriteLine("   estimate of storage costs for different access tiers (Hot, Cool, Archive)");
            Console.WriteLine("   should you decide to store the files in Azure Storage.");
            Console.WriteLine("2. Analyze one or more blob containers in your existing Azure storage account");
            Console.WriteLine("   and give you an estimate of storage costs for different access tiers. ");
            Console.WriteLine("   This tool can also move blobs across different access tiers for you,");
            Console.WriteLine("   should you choose.");
            Console.WriteLine();
            Console.WriteLine("For number 2, please note that currently this tool will only work when using");
            Console.WriteLine("a \"Blob Storage\" account with LRS redundancy in the \"US East 2\" region");
            Console.WriteLine("where the parent subscription is enabled for the archive feature.");
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
            filterCriteria = new Models.FilterCriteria()
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
                    matchingSummaryStatistics.Blobs.AddRange(matchingBlobStatistics.Blobs);
                    totalMatchingBlobs += matchingBlobStatistics.Count;
                    totalMatchingBlobsSize += matchingBlobStatistics.Size;
                    var blobsSizeCountString = $"{blobStatistics.Count}/{Helpers.Utils.SizeAsString(blobStatistics.Size)} ({blobStatistics.Size} bytes)";
                    var matchingBlobsSizeCountString = $"{matchingBlobStatistics.Count}/{Helpers.Utils.SizeAsString(matchingBlobStatistics.Size)} ({matchingBlobStatistics.Size} bytes)";
                    text = string.Format("{0, 12}{1, 40}{2, 40}", label, blobsSizeCountString, matchingBlobsSizeCountString);
                    Console.WriteLine(text);
                }
                Console.WriteLine(new string('-', text.Length));
            }
            Console.WriteLine();
            Console.WriteLine("Summary for all containers");
            var summaryText = string.Format("{0, 12}{1, 40}{2, 40}", "Access Tier", "Total Blobs Count/Size", "Matching Blobs Count/Size");
            Console.WriteLine(new string('-', summaryText.Length));
            Console.WriteLine(summaryText);
            Console.WriteLine(new string('-', summaryText.Length));
            foreach (var key in summaryContainerStats.BlobsStatistics.Keys)
            {
                var label = key.ToString();
                var blobStatistics = summaryContainerStats.BlobsStatistics[key];
                var matchingBlobStatistics = summaryContainerStats.MatchingBlobsStatistics[key];
                var blobsSizeCountString = $"{blobStatistics.Count}/{Helpers.Utils.SizeAsString(blobStatistics.Size)} ({blobStatistics.Size} bytes)";
                var matchingBlobsSizeCountString = $"{matchingBlobStatistics.Count}/{Helpers.Utils.SizeAsString(matchingBlobStatistics.Size)} ({matchingBlobStatistics.Size} bytes)";
                summaryText = string.Format("{0, 12}{1, 40}{2, 40}", label, blobsSizeCountString, matchingBlobsSizeCountString);
                Console.WriteLine(summaryText);
            }
            Console.WriteLine(new string('-', summaryText.Length));
            ChooseCostAnalysisOption(summaryContainerStats);
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
                sourceType = sourceTypeInput.Remove(0, SourceTypeArgumentName.Length);
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
                Console.WriteLine("Please note that you can also specify source type as command line argument.");
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
                folderName = folderPathArgument.Remove(0, SourceArgumentName.Length);
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
                Console.WriteLine("Please note that you can also specify folder path as command line argument.");
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
                connectionString = connectionStringArgument.Remove(0, ConnectionStringArgumentName.Length);
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
                Console.WriteLine("Please note that you can also specify connection string as command line argument.");
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
                containerName = containerArgument.Remove(0, SourceArgumentName.Length);
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
                Console.WriteLine("Please note that you can also specify container name as command line argument.");
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
                var daysValue = numDaysArgument.Remove(0, DaysArgumentName.Length);
                if (Int32.TryParse(daysValue, out numDays))
                {
                    if (numDays > 0) return numDays;
                }
            }
            Console.WriteLine();
            Console.WriteLine(new string('*', 30));
            Console.WriteLine("Enter the last modified time (in days) to exclude blobs/files modified after that point in time. If your blobs have never been modified, last modified time is equivalent to creation time. For example, specifying the value 30 will exclude all blobs created or modified in the last 30 days from analysis.");
            Console.WriteLine("Please note that you can also specify this value as command line argument.");
            Console.WriteLine("Simply specify /Days:<numberofdays> in command line.");
            Console.WriteLine(new string('*', 30));
            Console.WriteLine();
            var consoleInput = Console.ReadLine().Trim().ToLowerInvariant();
            ExitApplicationIfRequired(consoleInput);
            if (consoleInput == "")
            {
                return 30;
            }
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
                var sizeValue = sizeArgument.Remove(0, SizeArgumentName.Length);
                if (long.TryParse(sizeValue, out size))
                {
                    if (size >= 0) return size;
                }
            }
            Console.WriteLine();
            Console.WriteLine(new string('*', 30));
            Console.WriteLine("Enter the minimum size of the blob/file in bytes to be considered for analysis. Press \"Enter\" key for default value (0 bytes).");
            Console.WriteLine("Please note that you can also specify this value as command line argument.");
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

        private static void ChooseCostAnalysisOption(Models.ContainerStatistics containerStatisics)
        {
            Console.WriteLine("----------------------------------------------------------------------------------------------------------------");
            Console.WriteLine("Please select from one of the following options to perform cost analysis:");
            Console.WriteLine("Enter \"1\" to estimate cost of moving blobs from \"Hot\" access tier to \"Archive\" access tier.");
            Console.WriteLine("Enter \"2\" to estimate cost of moving blobs from \"Hot\" access tier to \"Cool\" access tier.");
            Console.WriteLine("Enter \"3\" to estimate cost of moving blobs from \"Cool\" access tier to \"Archive\" access tier.");
            Console.WriteLine("Enter \"4\" to estimate cost of moving blobs from \"Cool\" access tier to \"Hot\" access tier.");
            Console.WriteLine("Enter \"5\" to estimate cost of moving blobs from \"Archive\" access tier to \"Hot\" access tier.");
            Console.WriteLine("Enter \"6\" to estimate cost of moving blobs from \"Archive\" access tier to \"Cool\" access tier.");
            Console.WriteLine("Enter \"7\" to estimate cost of moving blobs from \"Hot\" and \"Cool\" access tier to \"Archive\" access tier.");
            Console.WriteLine("Enter \"8\" to estimate cost of moving blobs from \"Hot\" and \"Archive\" access tier to \"Cool\" access tier.");
            Console.WriteLine("Enter \"9\" to estimate cost of moving blobs from \"Cool\" and \"Archive\" access tier to \"Hot\" access tier.");
            Console.WriteLine("Enter \"R\" to rerun blobs analysis");
            Console.WriteLine("Enter \"X\" to terminate the application");
            Console.WriteLine("----------------------------------------------------------------------------------------------------------------");
            var consoleInput = Console.ReadLine();
            ExitApplicationIfRequired(consoleInput);
            List<StandardBlobTier> sourceTiers = new List<StandardBlobTier>();
            StandardBlobTier targetTier = StandardBlobTier.Unknown;
            var scenarioText = string.Empty;
            switch (consoleInput)
            {
                case "1":
                    scenarioText = "Move blobs from \"Hot\" access tier to \"Archive\" access tier";
                    sourceTiers = new List<StandardBlobTier>()
                    {
                        { StandardBlobTier.Hot }
                    };
                    targetTier = StandardBlobTier.Archive;
                    break;
                case "2":
                    scenarioText = "Move blobs from \"Hot\" access tier to \"Cool\" access tier.";
                    sourceTiers = new List<StandardBlobTier>()
                    {
                        { StandardBlobTier.Hot }
                    };
                    targetTier = StandardBlobTier.Cool;
                    break;
                case "3":
                    scenarioText = "Move blobs from \"Cool\" access tier to \"Archive\" access tier.";
                    sourceTiers = new List<StandardBlobTier>()
                    {
                        { StandardBlobTier.Cool }
                    };
                    targetTier = StandardBlobTier.Archive;
                    break;
                case "4":
                    scenarioText = "Move blobs from \"Cool\" access tier to \"Hot\" access tier.";
                    sourceTiers = new List<StandardBlobTier>()
                    {
                        { StandardBlobTier.Cool }
                    };
                    targetTier = StandardBlobTier.Hot;
                    break;
                case "5":
                    scenarioText = "Move blobs from \"Archive\" access tier to \"Hot\" access tier.";
                    sourceTiers = new List<StandardBlobTier>()
                    {
                        { StandardBlobTier.Archive }
                    };
                    targetTier = StandardBlobTier.Hot;
                    break;
                case "6":
                    scenarioText = "Move blobs from \"Archive\" access tier to \"Cool\" access tier.";
                    sourceTiers = new List<StandardBlobTier>()
                    {
                        { StandardBlobTier.Archive }
                    };
                    targetTier = StandardBlobTier.Cool;
                    break;
                case "7":
                    scenarioText = "Move blobs from \"Hot\" and \"Cool\" access tier to \"Archive\" access tier.";
                    sourceTiers = new List<StandardBlobTier>()
                    {
                        { StandardBlobTier.Hot },
                        { StandardBlobTier.Cool }
                    };
                    targetTier = StandardBlobTier.Archive;
                    break;
                case "8":
                    scenarioText = "Move blobs from \"Hot\" and \"Archive\" access tier to \"Cool\" access tier.";
                    sourceTiers = new List<StandardBlobTier>()
                    {
                        { StandardBlobTier.Hot },
                        { StandardBlobTier.Archive }
                    };
                    targetTier = StandardBlobTier.Cool;
                    break;
                case "9":
                    scenarioText = "Move blobs from \"Cool\" and \"Archive\" access tier to \"Hot\" access tier.";
                    sourceTiers = new List<StandardBlobTier>()
                    {
                        { StandardBlobTier.Archive },
                        { StandardBlobTier.Cool }
                    };
                    targetTier = StandardBlobTier.Hot;
                    break;
                case "r":
                case "R":
                    AnalyzeStorageAccount(sourcesToScan, filterCriteria);
                    break;
                default:
                    Console.WriteLine("Invalid input. Please try again.");
                    ChooseCostAnalysisOption(containerStatisics);
                    break;
            }
            if (targetTier != StandardBlobTier.Unknown)
            {
                DoCostAnalysisBlobs(scenarioText, sourceTiers, targetTier, containerStatisics);
            }
        }

        private static void DoCostAnalysisBlobs(string scenarioText, List<StandardBlobTier> sourceTiers, StandardBlobTier targetTier, Models.ContainerStatistics statistics)
        {
            var storageCostTargetTier = storageCosts[targetTier];
            double currentStorageCostTargetTier = statistics.MatchingBlobsStatistics[targetTier].Size / Helpers.Constants.GB * storageCostTargetTier.DataStorageCostPerGB;
            double currentStorageCosts = currentStorageCostTargetTier;
            long totalCount = statistics.MatchingBlobsStatistics[targetTier].Count;
            long totalSize = statistics.MatchingBlobsStatistics[targetTier].Size;
            double storageCostsAfterMove = currentStorageCostTargetTier;
            double dataTierChangeCost = 0.0;
            double dataRetrievalCost = 0.0;
            long totalMatchingBlobs = 0;
            Console.WriteLine($"Scenario: {scenarioText}");
            var header = string.Format("{0, 12}{1, 20}{2, 20}{3, 30}", "Access Tier", "Total Blobs Count", "Total Blobs Size", "Storage Costs (Per Month)");
            Console.WriteLine();
            Console.WriteLine("Current Storage Costs:");
            Console.WriteLine(new string('-', header.Length));
            Console.WriteLine(header);
            Console.WriteLine(new string('-', header.Length));

            foreach (var sourceTier in sourceTiers)
            {
                var storageCostSourceTier = storageCosts[sourceTier];
                var matchingItemFromStatistics = statistics.MatchingBlobsStatistics[sourceTier];
                totalMatchingBlobs += matchingItemFromStatistics.Count;
                totalCount += matchingItemFromStatistics.Count;
                totalSize += matchingItemFromStatistics.Size;
                var currentStorageCostSourceTier = matchingItemFromStatistics.Size / Helpers.Constants.GB * storageCostSourceTier.DataStorageCostPerGB;
                Console.WriteLine("{0, 12}{1,20}{2, 20}{3, 30}", sourceTier.ToString(), matchingItemFromStatistics.Count, Helpers.Utils.SizeAsString(matchingItemFromStatistics.Size), currentStorageCostSourceTier.ToString("C"));
                currentStorageCosts += currentStorageCostSourceTier;
                dataTierChangeCost += matchingItemFromStatistics.Count * storageCostSourceTier.WriteOperationsCostPerTenThousand / 10000;
                dataRetrievalCost += matchingItemFromStatistics.Size / Helpers.Constants.GB * storageCostSourceTier.DataRetrievalCostPerGB;
                storageCostsAfterMove += matchingItemFromStatistics.Size / Helpers.Constants.GB * storageCostTargetTier.DataStorageCostPerGB;
            }
            Console.WriteLine("{0, 12}{1,20}{2, 20}{3, 30}", targetTier.ToString(), statistics.MatchingBlobsStatistics[targetTier].Count, Helpers.Utils.SizeAsString(statistics.MatchingBlobsStatistics[targetTier].Size), currentStorageCostTargetTier.ToString("C"));
            Console.WriteLine(new string('-', header.Length));
            Console.WriteLine("{0, 12}{1,20}{2, 20}{3, 30}", "Total", totalCount, Helpers.Utils.SizeAsString(totalSize), currentStorageCosts.ToString("C"));
            Console.WriteLine(new string('-', header.Length));
            Console.WriteLine();
            Console.WriteLine("Storage Costs After Migration:");
            Console.WriteLine(new string('-', header.Length));
            Console.WriteLine(header);
            Console.WriteLine(new string('-', header.Length));
            foreach (var sourceTier in sourceTiers)
            {
                Console.WriteLine("{0, 12}{1,20}{2, 20}{3, 30}", sourceTier.ToString(), 0, Helpers.Utils.SizeAsString(0), 0.ToString("C"));
            }
            Console.WriteLine("{0, 12}{1,20}{2, 20}{3, 30}", targetTier.ToString(), totalCount, Helpers.Utils.SizeAsString(totalSize), storageCostsAfterMove.ToString("C"));
            Console.WriteLine(new string('-', header.Length));
            Console.WriteLine("{0, 12}{1,20}{2, 20}{3, 30}", "Total", totalCount, Helpers.Utils.SizeAsString(totalSize), storageCostsAfterMove.ToString("C"));
            Console.WriteLine(new string('-', header.Length));
            Console.WriteLine("{0, 62}{1,20}", "One time cost of changing blob access tier:", dataTierChangeCost.ToString("C"));
            Console.WriteLine("{0, 62}{1,20}", "One time cost of data retrieval for changing blob access tier:", dataRetrievalCost.ToString("C"));
            Console.WriteLine();
            Console.WriteLine($"To change the access tier of the blobs to \"{targetTier.ToString()}\", please enter \"Y\" now.");
            Console.WriteLine("Please be aware of the the one-time costs you will incur for changing the access tiers.");
            Console.WriteLine("Enter any other key to continue with the cost analysis.");
            var consoleInput = Console.ReadLine();
            switch (consoleInput)
            {
                case "y":
                case "Y":
                    Console.WriteLine($"Changing access tier of the blobs to \"{targetTier.ToString()}\". Please wait.");
                    double successCount = 0;
                    double failureCount = 0;
                    var matchingTargetTierFromStatistics = statistics.MatchingBlobsStatistics[targetTier];
                    foreach (var sourceTier in sourceTiers)
                    {
                        var matchingSourceItemFromStatistics = statistics.MatchingBlobsStatistics[sourceTier];
                        var blobs = matchingSourceItemFromStatistics.Blobs;
                        var blobsCount = blobs.Count;
                        for (var i=blobsCount-1; i>=0; i--)
                        {
                            var blob = blobs[i];
                            var result = Helpers.BlobStorageHelper.ChangeAccessTier(blob, targetTier).GetAwaiter().GetResult();
                            if (result)
                            {
                                blobs.Remove(blob);
                                matchingSourceItemFromStatistics.Count -= 1;
                                matchingSourceItemFromStatistics.Size -= blob.Properties.Length;
                                matchingTargetTierFromStatistics.Blobs.Add(blob);
                                matchingTargetTierFromStatistics.Count += 1;
                                matchingTargetTierFromStatistics.Size += blob.Properties.Length;
                                successCount += 1;
                            }
                            else
                            {
                                failureCount += 1;
                            }
                            Console.Write("\rSuccessful: {0, 12} Failure: {1, 12}", (successCount / totalMatchingBlobs).ToString("P"), (failureCount / totalMatchingBlobs).ToString("P"));
                        }
                    }
                    Console.WriteLine();
                    ChooseCostAnalysisOption(statistics);
                    break;
                default:
                    ChooseCostAnalysisOption(statistics);
                    break;
            }
        }
    }
}
