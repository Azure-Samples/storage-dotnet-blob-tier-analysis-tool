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
        private const string HelpArgumentName = "/?";
        private const string SourceTypeArgumentName = "/SourceType:";
        private const string ConnectionStringArgumentName = "/ConnectionString:";
        private const string SourceArgumentName = "/Source:";
        private const string DaysArgumentName = "/Days:";
        private const string SizeArgumentName = "/Size:";
        private const string TargetTierArgumentName = "/TargetTier:";
        private static Dictionary<StandardBlobTier, Models.StorageCosts> storageCosts = null;
        private static IEnumerable<string> sourcesToScan = null;
        private static Models.FilterCriteria filterCriteria = null;
        static void Main(string[] args)
        {
            Console.WriteLine("*********************************************************************************");
            Console.WriteLine("Welcome to the Blob Tier Analysis Tool. This tool can:");
            Console.WriteLine("1. Analyze the contents of a local folder/file share and give you an");
            Console.WriteLine("   estimate of storage costs for different access tiers (Hot, Cool, Archive)");
            Console.WriteLine("   should you decide to store the files in Azure Storage.");
            Console.WriteLine("2. Analyze one or more blob containers in your existing Azure storage account");
            Console.WriteLine("   and give you an estimate of storage costs for different access tiers. ");
            Console.WriteLine("3. Based on analysis, move blobs across different access tiers for you.");
            Console.WriteLine();
            Console.WriteLine("For number 3, please note that currently this tool will only work when using");
            Console.WriteLine("a \"Blob Storage\" account with LRS redundancy in the \"US East 2\" region");
            Console.WriteLine("where the parent subscription is enabled for the archive feature.");
            Console.WriteLine();
            Console.WriteLine("For more details on public preview of object-level tiering and archive storage,"); 
            Console.WriteLine("Please read https://azure.microsoft.com/en-us/blog/announcing-the-public-preview-of-azure-archive-blob-storage-and-blob-level-tiering/ for more details");
            Console.WriteLine();
            Console.WriteLine("*********************************************************************************");
            Console.WriteLine();
            GetHelpCommandLineArgument();
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
                    Console.WriteLine($"Listing blob containers in \"{Helpers.BlobStorageHelper.StorageAccount.Credentials.AccountName}\" storage account...");
                    sourcesToScan = Helpers.BlobStorageHelper.ListContainers().GetAwaiter().GetResult();
                }
                else
                {
                    sourcesToScan = new List<string>()
                    {
                        {containerToSearch}
                    };
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
                Console.WriteLine($"Analyzing files in \"{folderName}\" folder...");
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
            Console.WriteLine("Scenario 1: Upload files to Azure Storage and keep all blobs in \"Hot\" access tier");
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
            Console.WriteLine("Scenario 2: Upload files to Azure Storage and keep all blobs in \"Cool\" access tier");
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
            Console.WriteLine("Scenario 3: Upload files to Azure Storage and keep all blobs in \"Archive\" access tier");
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
            Console.WriteLine("*All currency values are rounded to the nearest cent.");
            Console.WriteLine("*The pricing displayed above is based on storage pricing in \"US East 2\" region.");
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
                Console.WriteLine($"Analyzing blobs in \"{containerName}\" blob container...");
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
                    matchingSummaryStatistics.BlobNames.AddRange(matchingBlobStatistics.BlobNames);
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
            DoBlobsCostAnalysis(containersStats);
        }

        private static void GetHelpCommandLineArgument()
        {
            var helpCommandLineArgument = TryParseCommandLineArgumentsToExtractValue(HelpArgumentName);
            if (!string.IsNullOrWhiteSpace(helpCommandLineArgument))
            {
                Console.WriteLine("You can run this application in non-interactive mode");
                Console.WriteLine();
                Console.WriteLine("Usage:");
                Console.WriteLine("BlobTierAnalysisTool </SourceType:> </ConnectionString:> </Source:> </Days:> </Size:> </TargetTier:>");
                Console.WriteLine();
                Console.WriteLine("/SourceType:<Either [L]ocal or [C]loud>. Specifies the type of source you want to analyze.");
                Console.WriteLine("/ConnectionString:<Storage account connection string>. Specifies storage account connection string.");
                Console.WriteLine("/Source:<Analysis source>. Specifies analysis source. For \"Local\" source type, it should be the folder path and for \"Cloud\" source type, it could either be the name of a blob container or \"*\" for all blob containers.");
                Console.WriteLine("/Days:<Last modified time in days>. Specifies the minimum last modified time (in days before the present time) of a blob / local file to be considered for analysis. Must be a value greater than on equal to zero (0).");
                Console.WriteLine("/Size:<Minimum file size>. Specifies the minimum size of a blob / local file to be considered for analysis. Must be a value greater than on eqal to zero (0).");
                Console.WriteLine("/TargetTier:<Either [H]ot, [C]ool or [A]rchive>. Specifies the target tier for cost calculations.");
                Console.WriteLine("/?. Displays the help for command line arguments.");
                Console.WriteLine();
                Console.WriteLine("Examples:");
                Console.WriteLine();
                var str = "Analyze a local folder";
                Console.WriteLine(str);
                Console.WriteLine(new string('-', str.Length));
                Console.WriteLine("BlobTierAnalysisTool /SourceType:L /Source:C:\temp /Days:30 /Size:1024");
                Console.WriteLine("Analyzes all files in \"C:\temp\" directory that have not been modified for the last 30 days and are greater than or equal to 1KB in size");
                Console.WriteLine();
                str = "Analyze a blob container in a storage account.";
                Console.WriteLine(str);
                Console.WriteLine(new string('-', str.Length));
                Console.WriteLine("BlobTierAnalysisTool /SourceType:C /ConnectionString:DefaultEndpointsProtocol=https;AccountName=accountname;AccountKey=accountkey==;EndpointSuffix=core.windows.net /Source:temp /Days:30 /Size:1024");
                Console.WriteLine("Analyzes all blobs in \"temp\" blob container in the \"accountname\" storage account that have not been modified for last 30 days and are greater than or equal to 1KB in size");
                Console.WriteLine();
                str = "Analyze all blob containers in a storage account.";
                Console.WriteLine(str);
                Console.WriteLine(new string('-', str.Length));
                Console.WriteLine("BlobTierAnalysisTool /SourceType:C /ConnectionString:DefaultEndpointsProtocol=https;AccountName=accountname;AccountKey=accountkey==;EndpointSuffix=core.windows.net /Source:* /Days:30 /Size:1024");
                Console.WriteLine("Analyzes all blobs in all blob containers in \"accountname\" storage account that have not been modified for last 30 days and are greater than or equal to 1KB in size");
                Console.WriteLine();
                ExitApplicationIfRequired("X");
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
                Console.WriteLine("Enter the source type you want to analyze. Valid values are [L]ocal or [C]loud.");
                Console.WriteLine("To exit the application, enter \"X\"");
                Console.WriteLine(new string('*', 30));
                sourceType = Console.ReadLine().ToUpperInvariant();
                ExitApplicationIfRequired(sourceType);
                if (!string.IsNullOrWhiteSpace(sourceType) && (sourceType == "C" || sourceType == "L"))
                {
                    return sourceType;
                }
                else
                {
                    Console.WriteLine("Invalid input. Specify either \"L\" or \"C\" as source type");
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
                Console.WriteLine("To exit the application, enter \"X\"");
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
                Console.WriteLine("Enter connection string for your storage account in the following format: DefaultEndpointsProtocol=https;AccountName=<youraccountname>;AccountKey=<youraccountkey>");
                Console.WriteLine("To exit the application, enter \"X\"");
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
                if (containerName != "*")
                {
                    Console.WriteLine($"Checking if \"{containerName}\" blob container exists in the storage account...");
                    if (!Helpers.BlobStorageHelper.DoesContainerExists(containerName).GetAwaiter().GetResult())
                    {
                        Console.WriteLine("Specified blob container does not exist. Please modify the command line arguments and try again. Terminating application.");
                        Console.WriteLine("Press any key to terminate the application");
                        Console.ReadKey();
                        ExitApplicationIfRequired("X");
                    }
                }
                return containerName;
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine(new string('*', 30));
                Console.WriteLine("Enter the blob container name that you want to analyze. To analyze all blob containers, enter \"*\".");
                Console.WriteLine("Press the \"Enter\" key for default value (*, all containers)");
                Console.WriteLine("To exit the application, enter \"X\"");
                Console.WriteLine(new string('*', 30));
                Console.WriteLine();
                containerName = Console.ReadLine().Trim().ToLowerInvariant();
                ExitApplicationIfRequired(containerName);
                if (string.IsNullOrWhiteSpace(containerName))
                {
                    containerName = "*";
                }
                if (!string.IsNullOrWhiteSpace(containerName) && Helpers.Utils.IsValidContainerName(containerName))
                {
                    if (containerName != "*")
                    {
                        Console.WriteLine($"Checking if \"{containerName}\" blob container exists in the storage account...");
                        if (!Helpers.BlobStorageHelper.DoesContainerExists(containerName).GetAwaiter().GetResult())
                        {
                            Console.WriteLine("Specified blob container does not exist. Please try again.");
                            return GetContainerInput();
                        }
                    }
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
                    if (numDays >= 0) return numDays;
                }
            }
            Console.WriteLine();
            Console.WriteLine(new string('*', 30));
            Console.WriteLine("Enter the minimum last modified time (in days before the present time) of a blob / local file to be considered for analysis.");
            Console.WriteLine("If your blobs have never been modified, last modified time is equivalent to creation time.");
            Console.WriteLine("For example, specifying the value 30 will exclude all blobs created or modified in the last 30 days from analysis.");
            Console.WriteLine("Press the \"Enter\" key for default value (30 days).");
            Console.WriteLine("To exit the application, enter \"X\"");
            Console.WriteLine(new string('*', 30));
            Console.WriteLine();
            var consoleInput = Console.ReadLine().Trim().ToLowerInvariant();
            ExitApplicationIfRequired(consoleInput);
            if (consoleInput == "")
            {
                return 30;
            }
            if (!Int32.TryParse(consoleInput, out numDays) || numDays < 0)
            {
                Console.WriteLine("Invalid input for last modified time. Enter a valid numeric value greater than or equal to zero (0).");
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
            Console.WriteLine("Enter the minimum size of a blob / file (in bytes) to be considered for analysis.");
            Console.WriteLine("Press the \"Enter\" key for default value (0 bytes).");
            Console.WriteLine("To exit the application, enter \"X\"");
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
                Console.WriteLine("Invalid blob/file size input. Please enter a valid numeric value greater than or equal to zero (0).");
                return GetBlobOrFileSizeFilterCriteriaInput();
            }
            return size;
        }

        /// <summary>
        /// Reads the target blob tier from command line arguments or via user input. This is used for doing 
        /// cost calculation.
        /// </summary>
        /// <param name="targetTierInput"></param>
        /// <returns>Target blob tier.</returns>
        private static StandardBlobTier GetTargetBlobTierInput(string targetTierInput)
        {
            var targetTier = StandardBlobTier.Unknown;
            if (string.IsNullOrWhiteSpace(targetTierInput))
            {
                Console.WriteLine(new string('*', 30));
                Console.WriteLine("Enter the target tier for analysis. Valid values are \"A\" (archive tier), \"C\" (cool tier) or \"H\" (hot tier).");
                Console.WriteLine("To exit the application, enter \"X\"");
                Console.WriteLine(new string('*', 30));
                Console.WriteLine();
                targetTierInput = Console.ReadLine().Trim().ToUpperInvariant();
                ExitApplicationIfRequired(targetTierInput);
            }
            switch (targetTierInput)
            {
                case "A":
                    targetTier = StandardBlobTier.Archive;
                    break;
                case "C":
                    targetTier = StandardBlobTier.Cool;
                    break;
                case "H":
                    targetTier = StandardBlobTier.Hot;
                    break;
                default:
                    Console.WriteLine("Invalid target tier input. Please enter one of the following values: \"A\", \"C\" or \"H\".");
                    return GetTargetBlobTierInput("");
            }
            return targetTier;
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

        private static void DoBlobsCostAnalysis(List<Models.ContainerStatistics> statistics)
        {
            var targetTierInput = TryParseCommandLineArgumentsToExtractValue(TargetTierArgumentName);
            if (!String.IsNullOrWhiteSpace(targetTierInput))
            {
                targetTierInput = targetTierInput.Remove(0, TargetTierArgumentName.Length);
            }
            StandardBlobTier targetTier = GetTargetBlobTierInput(targetTierInput);
            List<StandardBlobTier> sourceTiers = new List<StandardBlobTier>()
            {
                StandardBlobTier.Hot,
                StandardBlobTier.Cool,
                StandardBlobTier.Archive
            };
            sourceTiers.Remove(targetTier);
            var scenarioText = $"Move blobs from other access tiers to \"{targetTier.ToString()}\" access tier.";
            var storageCostTargetTier = storageCosts[targetTier];
            double currentStorageCosts = 0;
            long totalCount = 0;
            long totalSize = 0;
            double storageCostsAfterMove = 0.0;
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
                double currentStorageCostSourceTier = 0.0;
                long totalCountSourceTier = 0;
                long totalSizeSourceTier = 0;
                foreach (var containerStatistics in statistics)
                {
                    var matchingItem = containerStatistics.MatchingBlobsStatistics[sourceTier];
                    totalCountSourceTier += matchingItem.Count;
                    totalSizeSourceTier += matchingItem.Size;
                    currentStorageCostSourceTier += matchingItem.Size / Helpers.Constants.GB * storageCostSourceTier.DataStorageCostPerGB;
                }
                Console.WriteLine("{0, 12}{1,20}{2, 20}{3, 30}", sourceTier.ToString(), totalCountSourceTier, Helpers.Utils.SizeAsString(totalSizeSourceTier), currentStorageCostSourceTier.ToString("C"));
                currentStorageCosts += currentStorageCostSourceTier;
                totalMatchingBlobs += totalCountSourceTier;
                totalCount += totalCountSourceTier;
                totalSize += totalSizeSourceTier;
                dataTierChangeCost += totalCountSourceTier * storageCostSourceTier.WriteOperationsCostPerTenThousand / 10000;
                dataRetrievalCost += totalSizeSourceTier / Helpers.Constants.GB * storageCostSourceTier.DataRetrievalCostPerGB;
                storageCostsAfterMove += totalSizeSourceTier / Helpers.Constants.GB * storageCostTargetTier.DataStorageCostPerGB;
            }
            long totalCountTargetTier = 0;
            long totalSizeTargetTier = 0;
            double currentStorageCostTargetTier = 0.0;
            foreach (var containerStatistics in statistics)
            {
                var matchingItem = containerStatistics.MatchingBlobsStatistics[targetTier];
                totalCountTargetTier += matchingItem.Count;
                totalSizeTargetTier += matchingItem.Size;
                currentStorageCostTargetTier += matchingItem.Size / Helpers.Constants.GB * storageCostTargetTier.DataStorageCostPerGB;
            }
            currentStorageCosts += currentStorageCostTargetTier;
            totalCount += totalCountTargetTier;
            totalSize += totalSizeTargetTier;
            Console.WriteLine("{0, 12}{1,20}{2, 20}{3, 30}", targetTier.ToString(), totalCountTargetTier, Helpers.Utils.SizeAsString(totalSizeTargetTier), currentStorageCostTargetTier.ToString("C"));
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
            Console.WriteLine("{0, 12}{1,20}{2, 20}{3, 30}", "Savings", "--", "--", (storageCostsAfterMove - currentStorageCosts).ToString("C"));
            Console.WriteLine(new string('-', header.Length));
            Console.WriteLine("{0, 62}{1,20}", "One time cost of data retrieval and changing blob access tier:", (dataTierChangeCost + dataRetrievalCost).ToString("C"));
            Console.WriteLine("{0, 62}{1,20}", "Net Savings:", (storageCostsAfterMove - currentStorageCosts + dataTierChangeCost + dataRetrievalCost).ToString("C"));
            Console.WriteLine(new string('-', header.Length));
            Console.WriteLine();
            Console.WriteLine("*All currency values are rounded to the nearest cent.");
            Console.WriteLine("*The pricing displayed above is based on storage pricing in \"US East 2\" region.");
            Console.WriteLine();
            Console.WriteLine($"To change the access tier of the blobs to \"{targetTier.ToString()}\", enter \"Y\" now. Enter any other key to terminate the application.");
            Console.WriteLine("*Please be aware of the the one-time costs you will incur for changing the access tiers.");
            var consoleInput = Console.ReadLine().ToUpperInvariant();
            switch (consoleInput)
            {
                case "Y":
                    Console.WriteLine($"Changing access tier of the blobs to \"{targetTier.ToString()}\"...");
                    double successCount = 0;
                    double failureCount = 0;
                    foreach (var sourceTier in sourceTiers)
                    {
                        foreach (var containerStatistics in statistics)
                        {
                            var matchingItem = containerStatistics.MatchingBlobsStatistics[sourceTier];
                            var blobNames = matchingItem.BlobNames;
                            foreach (var blobName in blobNames)
                            {
                                var result = Helpers.BlobStorageHelper.ChangeAccessTier(containerStatistics.Name, blobName, targetTier).GetAwaiter().GetResult();
                                if (result)
                                {
                                    successCount += 1;
                                }
                                else
                                {
                                    failureCount += 1;
                                }
                                Console.Write("\rSuccessful: {0, 12} Failure: {1, 12}", (successCount / totalMatchingBlobs).ToString("P"), (failureCount / totalMatchingBlobs).ToString("P"));
                            }
                        }
                    }
                    Console.WriteLine();
                    if (successCount > 0)
                    {
                        Console.WriteLine($"Successully moved {successCount} to {targetTier.ToString()}.");
                    }
                    Console.WriteLine("Press any key to terminate the application.");
                    Console.ReadKey();
                    ExitApplicationIfRequired("X");
                    break;
                default:
                    ExitApplicationIfRequired("X");
                    break;
            }
        }
    }
}
