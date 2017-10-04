using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.WindowsAzure.Storage;
using System.Globalization;

namespace BlobTierAnalysisTool
{
    class Program
    {
        private const string HelpArgumentName = "/?";
        private const string StorageRegionArgumentName = "/Region:";
        private const string SourceTypeArgumentName = "/SourceType:";
        private const string ConnectionStringArgumentName = "/ConnectionString:";
        private const string SourceArgumentName = "/Source:";
        private const string DaysArgumentName = "/Days:";
        private const string SizeArgumentName = "/Size:";
        private const string TargetTierArgumentName = "/TargetTier:";
        private const string ReadPercentagePerMonthArgumentName = "/ReadPercentagePerMonth:";
        private const string ShowContainerLevelStatisticsArgumentName = "/ShowContainerLevelStatistics:";
        private const string AutoChangeTierArgumentName = "/AutoChangeTier:";

        private static Dictionary<StandardBlobTier, Models.StorageCosts> storageCosts = null;
        private static IEnumerable<string> sourcesToScan = null;
        private static Models.FilterCriteria filterCriteria = null;
        private static bool showContainerLevelStatistics = false;
        private static bool autoChangeTier = false;
        private static bool promptForChangeTier = false;

        static void Main(string[] args)
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            Console.WriteLine(new string('*', 80));
            Console.WriteLine("Welcome to the Blob Tier Analysis Tool. This tool can:");
            Console.WriteLine();
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
            Console.WriteLine("please read https://azure.microsoft.com/en-us/blog/announcing-the-public-preview-of-azure-archive-blob-storage-and-blob-level-tiering/.");
            Console.WriteLine();
            Console.WriteLine(new string('*', 80));
            Console.WriteLine();
            GetHelpCommandLineArgument();

            //storageCosts = new Dictionary<StandardBlobTier, Models.StorageCosts>()
            //{
            //    { StandardBlobTier.Hot, new Models.StorageCosts(0.0184, 0.05, 0.004, 0, 0) },
            //    { StandardBlobTier.Cool, new Models.StorageCosts(0.01, 0.10, 0.01, 0.01, 0.0025) },
            //    { StandardBlobTier.Archive, new Models.StorageCosts(0.0018, 0.30, 0.15, 0.0015, 0) }
            //};
            showContainerLevelStatistics = GetShowContainerLevelStatistics();
            storageCosts = GetStorageCostsBasedOnStorageRegionInput();
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
                if (!Helpers.BlobStorageHelper.ValidateConnection(containerToSearch).GetAwaiter().GetResult())
                {
                    Console.WriteLine("Unable to connect to storage account using the connection string provided. Please check the connection string and try again.");
                    ExitApplicationIfRequired("X");
                }
                if (containerToSearch == "*")
                {
                    Console.WriteLine($"Listing blob containers in storage account...");
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
            var readPercentagePerMonth = GetReadPercentagePerMonthInput();
            var numDaysSinceLastModifiedFilterCriteria = GetBlobOrFileLastModifiedDateFilterCriteriaInput();
            var blobSizeFilterCriteria = GetBlobOrFileSizeFilterCriteriaInput();
            filterCriteria = new Models.FilterCriteria()
            {
                MinBlobSize = blobSizeFilterCriteria,
                LastModifiedDateTo = DateTime.UtcNow.Date.AddDays(0 - numDaysSinceLastModifiedFilterCriteria)
            };
            if (sourceType == "L")
            {
                AnalyzeLocalStorage(sourcesToScan, filterCriteria, readPercentagePerMonth);
            }
            else
            {
                AnalyzeStorageAccount(sourcesToScan, filterCriteria, readPercentagePerMonth);
            }

            Console.WriteLine("Press any key to terminate the application.");
            Console.ReadLine();
        }

        private static void AnalyzeLocalStorage(IEnumerable<string> folderNames, Models.FilterCriteria filterCriteria, double readPercentagePerMonth)
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
                var filesSizeCountString = $"{folderStatistics.FilesStatistics.Count}/{Helpers.Utils.SizeAsString(folderStatistics.FilesStatistics.Size)}";
                var matchingFilesSizeCountString = $"{folderStatistics.MatchingFilesStatistics.Count}/{Helpers.Utils.SizeAsString(folderStatistics.MatchingFilesStatistics.Size)}";
                text = string.Format("{0, 40}{1, 40}", filesSizeCountString, matchingFilesSizeCountString);
                Console.WriteLine(text);
                Console.WriteLine(new string('-', text.Length));
            }

            Console.WriteLine();
            Console.WriteLine("Summary");
            text = string.Format("{0, 40}{1, 40}", "Total Files Count/Size", "Matching Files Count/Size");
            var filesSizeCountSummaryString = $"{summaryFoldersStatistics.FilesStatistics.Count}/{Helpers.Utils.SizeAsString(summaryFoldersStatistics.FilesStatistics.Size)}";
            var matchingFilesSizeSummaryCountString = $"{summaryFoldersStatistics.MatchingFilesStatistics.Count}/{Helpers.Utils.SizeAsString(summaryFoldersStatistics.MatchingFilesStatistics.Size)}";
            text = string.Format("{0, 40}{1, 40}", filesSizeCountSummaryString, matchingFilesSizeSummaryCountString);
            Console.WriteLine(text);
            Console.WriteLine(new string('-', text.Length));
            Console.WriteLine();
            Console.WriteLine("Cost Estimator");
            Console.WriteLine();
            Console.WriteLine("Scenario 1: Upload files to Azure Storage and keep all blobs in \"Hot\" access tier");
            var storageCostsHotAccessTier = storageCosts[StandardBlobTier.Hot];
            var storageCostsCoolAccessTier = storageCosts[StandardBlobTier.Cool];
            var storageCostsArchiveAccessTier = storageCosts[StandardBlobTier.Archive];
            var totalFiles = summaryFoldersStatistics.MatchingFilesStatistics.Count;
            var totalSize = summaryFoldersStatistics.MatchingFilesStatistics.Size;
            var readOperations = totalSize * readPercentagePerMonth / 100;
            var averageFileSize = (double)totalSize / totalFiles;
            var readTransactions = readOperations / averageFileSize;
            double writeTransactionsCost, storageCost, dataTierChangeCost;
            if (storageCostsHotAccessTier != null)
            {
                writeTransactionsCost = storageCostsHotAccessTier.WriteOperationsCostPerTenThousand * totalFiles / 10000;
                storageCost = storageCostsHotAccessTier.DataStorageCostPerGB * totalSize / Helpers.Constants.GB;
                //For calculation of read costs in "Hot" access tier, this is the formula used:
                //read cost = # of blobs read x read transaction cost / 10000.
                var totalReadsCostPerMonth = CalculateReadsCost(StandardBlobTier.Hot, readTransactions, readOperations, storageCosts);
                Console.WriteLine(new string('-', 95));
                Console.WriteLine("{0, 70}{1, 20}", "One time cost of uploading files in Azure Storage:", writeTransactionsCost.ToString("C"));
                Console.WriteLine(new string('-', 95));
                Console.WriteLine("{0, 70}{1, 20}", "Storage cost/month:", storageCost.ToString("C"));
                Console.WriteLine("{0, 70}{1, 20}", "Blob reads cost/month:", totalReadsCostPerMonth.HasValue ? totalReadsCostPerMonth.Value.ToString("C") : "--");
                Console.WriteLine(new string('-', 95));
                Console.WriteLine("{0, 70}{1, 20}", "Total cost/month:", (storageCost + totalReadsCostPerMonth.GetValueOrDefault()).ToString("C"));
                Console.WriteLine(new string('-', 95));
            }
            else
            {
                Console.WriteLine("Either \"Hot\" access tier is not available for the selected region or pricing is not defined for \"Hot\" access tier in the region file.");
            }
            Console.WriteLine();
            Console.WriteLine("Scenario 2: Upload files to Azure Storage and keep all blobs in \"Cool\" access tier");
            if (storageCostsCoolAccessTier != null)
            {
                writeTransactionsCost = storageCostsCoolAccessTier.WriteOperationsCostPerTenThousand * totalFiles / 10000;
                storageCost = storageCostsCoolAccessTier.DataStorageCostPerGB * totalSize / Helpers.Constants.GB;
                var totalReadsCostPerMonth = CalculateReadsCost(StandardBlobTier.Cool, readTransactions, readOperations, storageCosts);
                Console.WriteLine(new string('-', 95));
                Console.WriteLine("{0, 70}{1, 20}", "One time cost of uploading files in Azure Storage:", writeTransactionsCost.ToString("C"));
                Console.WriteLine(new string('-', 95));
                Console.WriteLine("{0, 70}{1, 20}", "Storage cost/month:", storageCost.ToString("C"));
                Console.WriteLine("{0, 70}{1, 20}", "Blob reads cost/month:", totalReadsCostPerMonth.HasValue ? totalReadsCostPerMonth.Value.ToString("C") : "--");
                Console.WriteLine(new string('-', 95));
                Console.WriteLine("{0, 70}{1, 20}", "Total cost/month:", (storageCost + totalReadsCostPerMonth.GetValueOrDefault()).ToString("C"));
                Console.WriteLine(new string('-', 95));
            }
            else
            {
                Console.WriteLine("Either \"Cool\" access tier is not available for the selected region or pricing is not defined for \"Cool\" access tier in the region file.");
            }
            Console.WriteLine();
            Console.WriteLine("Scenario 3: Upload files to Azure Storage and keep all blobs in \"Archive\" access tier");
            if (storageCostsArchiveAccessTier != null)
            {
                writeTransactionsCost = storageCostsHotAccessTier == null ? 0 : storageCostsHotAccessTier.WriteOperationsCostPerTenThousand * totalFiles / 10000;
                storageCost = storageCostsArchiveAccessTier.DataStorageCostPerGB * totalSize / Helpers.Constants.GB;
                dataTierChangeCost = storageCostsArchiveAccessTier.WriteOperationsCostPerTenThousand * totalFiles / 10000;
                var totalReadsCostPerMonth = CalculateReadsCost(StandardBlobTier.Archive, readTransactions, readOperations, storageCosts);
                Console.WriteLine(new string('-', 95));
                Console.WriteLine("{0, 70}{1, 20}", "One time cost of uploading files in Azure Storage:", writeTransactionsCost.ToString("C"));
                Console.WriteLine("{0, 70}{1, 20}", "One time cost of changing access tier from \"Hot\" to \"Archive\":", dataTierChangeCost.ToString("C"));
                Console.WriteLine(new string('-', 95));
                Console.WriteLine("{0, 70}{1, 20}", "Storage costs/month:", storageCost.ToString("C"));
                Console.WriteLine("{0, 70}{1, 20}", "Blob reads cost/month:", totalReadsCostPerMonth.HasValue ? totalReadsCostPerMonth.Value.ToString("C") : "--");
                Console.WriteLine(new string('-', 95));
                Console.WriteLine("{0, 70}{1, 20}", "Total cost/month:", (storageCost + totalReadsCostPerMonth.GetValueOrDefault()).ToString("C"));
                Console.WriteLine(new string('-', 95));
            }
            else
            {
                Console.WriteLine("Either \"Archive\" access tier is not available for the selected region or pricing is not defined for \"Archive\" access tier in the region file.");
            }
            Console.WriteLine();
            Console.WriteLine("*All currency values are rounded to the nearest cent and are in US Dollars ($).");
            Console.WriteLine();
            Console.WriteLine("*Read costs do not include data egress charges, as these will only be assessed when data is read outside of the Azure region.");
            Console.WriteLine();
            Console.WriteLine("*Storage costs does not include charge for metadata storage.");
            Console.WriteLine();
            Console.WriteLine("*For \"Archive\" tier, preview prices are being used. Please note that it may take up to 15 hours to read a blob from this tier.");
            Console.WriteLine();
        }

        private static void AnalyzeStorageAccount(IEnumerable<string> containerNames, Models.FilterCriteria filterCriteria, double readPercentagePerMonth)
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
                var text = string.Format("{0, 12}{1, 50}{2, 50}", "Access Tier", "Total Block Blobs Count/Size", "Matching Block Blobs Count/Size");
                if (showContainerLevelStatistics)
                {
                    Console.WriteLine(new string('-', text.Length));
                    Console.WriteLine(text);
                    Console.WriteLine(new string('-', text.Length));
                }
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
                    if (showContainerLevelStatistics)
                    {
                        var blobsSizeCountString = $"{blobStatistics.Count}/{Helpers.Utils.SizeAsString(blobStatistics.Size)}";
                        var matchingBlobsSizeCountString = $"{matchingBlobStatistics.Count}/{Helpers.Utils.SizeAsString(matchingBlobStatistics.Size)}";
                        text = string.Format("{0, 12}{1, 50}{2, 50}", label, blobsSizeCountString, matchingBlobsSizeCountString);
                        Console.WriteLine(text);
                    }
                }
                if (showContainerLevelStatistics)
                {
                    Console.WriteLine(new string('-', text.Length));
                }
            }
            Console.WriteLine();
            Console.WriteLine("Summary for all containers");
            var summaryText = string.Format("{0, 12}{1, 50}{2, 50}", "Access Tier", "Total Block Blobs Count/Size", "Matching Block Blobs Count/Size");
            Console.WriteLine(new string('-', summaryText.Length));
            Console.WriteLine(summaryText);
            Console.WriteLine(new string('-', summaryText.Length));
            foreach (var key in summaryContainerStats.BlobsStatistics.Keys)
            {
                var label = key.ToString();
                var blobStatistics = summaryContainerStats.BlobsStatistics[key];
                var matchingBlobStatistics = summaryContainerStats.MatchingBlobsStatistics[key];
                var blobsSizeCountString = $"{blobStatistics.Count}/{Helpers.Utils.SizeAsString(blobStatistics.Size)}";
                var matchingBlobsSizeCountString = $"{matchingBlobStatistics.Count}/{Helpers.Utils.SizeAsString(matchingBlobStatistics.Size)}";
                summaryText = string.Format("{0, 12}{1, 50}{2, 50}", label, blobsSizeCountString, matchingBlobsSizeCountString);
                Console.WriteLine(summaryText);
            }
            Console.WriteLine(new string('-', summaryText.Length));
            DoBlobsCostAnalysis(containersStats, readPercentagePerMonth);
        }

        private static void GetHelpCommandLineArgument()
        {
            var helpCommandLineArgument = TryParseCommandLineArgumentsToExtractValue(HelpArgumentName);
            if (!string.IsNullOrWhiteSpace(helpCommandLineArgument))
            {
                Console.WriteLine("You can run this application in non-interactive mode");
                Console.WriteLine();
                Console.WriteLine("Usage:");
                Console.WriteLine("BlobTierAnalysisTool </SourceType:> </ConnectionString:> </Source:> </Days:> </Size:> </TargetTier:> </Region:> </ReadPercentagePerMonth:> </ShowContainerLevelStatistics:> </AutoChangeTier>");
                Console.WriteLine();
                Console.WriteLine("/SourceType:<Either [L]ocal or [C]loud>. Specifies the type of source you want to analyze.");
                Console.WriteLine("/ConnectionString:<Storage account connection string or Account Shared Access Signature (SAS) URL>. Specifies storage account connection string or an Account Shared Access Signature.");
                Console.WriteLine("/Source:<Analysis source>. Specifies analysis source. For \"Local\" source type, it should be the folder path and for \"Cloud\" source type, it could either be the name of a blob container or \"*\" for all blob containers.");
                Console.WriteLine("/Days:<Last modified time in days>. Specifies the minimum last modified time (in days before the present time) of a blob / local file to be considered for analysis. Must be a value greater than on equal to zero (0).");
                Console.WriteLine("/Size:<Minimum file size>. Specifies the minimum size of a blob / local file to be considered for analysis. Must be a value greater than on eqal to zero (0).");
                Console.WriteLine("/TargetTier:<Either [H]ot, [C]ool or [A]rchive>. Specifies the target tier for cost calculations.");
                Console.WriteLine("/Region:<Storage account region.>. Specifies the region for the storage account. Must be one of the following values: AustraliaEast, AustraliaSouthEast, BrazilSouth, CanadaCentral, CanadaEast, CentralIndia, CentralUS, EastAsia, EastUS, EastUS2, JapanEast, JapanWest, KoreaCentral, KoreaSouth, NorthCentralEurope, NorthCentralUS, SouthCentralUS, SouthIndia, SouthEastAsia, UKSouth, UKWest, WestCentralUS, WestEurope, WestUS, WestUS2");
                Console.WriteLine("/ReadPercentagePerMonth:<data reads percentage>. Specifies the % of data that will be read per month. A value of 100% would mean that all data in the storage account will be read once per month. A value of 200% would mean that all data in the storage account will be read twice per month.");
                Console.WriteLine("/ShowContainerLevelStatistics:<true or false>. Indicates if container level statistics must be shown on console output.");
                Console.WriteLine("/AutoChangeTier:<true or false>. Indicates if the tool should automatically change the tier of the blob.");
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

        private static Dictionary<StandardBlobTier, Models.StorageCosts> GetStorageCostsBasedOnStorageRegionInput()
        {
            Dictionary<StandardBlobTier, Models.StorageCosts> storageCosts = null;
            var regionInput = TryParseCommandLineArgumentsToExtractValue(StorageRegionArgumentName);
            var region = string.Empty;
            if (!string.IsNullOrWhiteSpace(regionInput))
            {
                region = regionInput.Remove(0, StorageRegionArgumentName.Length);
            }
            else
            {
                Console.WriteLine(new string('*', 80));
                Console.WriteLine("Please specify the region for your storage account.");
                Console.WriteLine("If you are using this tool to analyze cost of uploading local files to Azure Storage, this is the region of the target account.");
                Console.WriteLine("If you are using this tool to analyze cost of tiering files already in Azure Storage, this is the region of your source account.");
                Console.WriteLine("Valid values are: AustraliaEast, AustraliaSouthEast, BrazilSouth, CanadaCentral, CanadaEast, CentralIndia, CentralUS, EastAsia, EastUS, EastUS2, JapanEast, JapanWest, KoreaCentral, KoreaSouth, NorthCentralEurope, NorthCentralUS, SouthCentralUS, SouthIndia, SouthEastAsia, UKSouth, UKWest, WestCentralUS, WestEurope, WestUS, WestUS2");
                Console.WriteLine("Press \"Enter\" key for default region (US East 2) or \"X\" to terminate the application.");
                Console.WriteLine(new string('*', 80));
                region = Console.ReadLine();
                ExitApplicationIfRequired(region);
            }
            if (region == "") region = "EastUS2";
            var storageCostsDataFilePath = Path.Combine(AppContext.BaseDirectory, "Data", $"{region.ToLowerInvariant()}.json");
            if (!File.Exists(storageCostsDataFilePath))
            {
                Console.WriteLine("Either invalid region specified or storage costs for that region are not defined.");
                if (!string.IsNullOrWhiteSpace(regionInput))
                {
                    Console.WriteLine("Application will terminate now.");
                    Console.WriteLine("Press any key to terminate the application.");
                    Console.ReadKey();
                    ExitApplicationIfRequired("X");
                }
                else
                {
                    Console.WriteLine("Please try again.");
                    return GetStorageCostsBasedOnStorageRegionInput();
                }
            }
            else
            {
                storageCosts = ReadStorageCostsFromDataFile(storageCostsDataFilePath);
                if (storageCosts == null)
                {
                    Console.WriteLine($"Unable to read storage costs from data file. Please check the contents of \"{storageCostsDataFilePath}\" file. Application will terminate now.");
                    ExitApplicationIfRequired("X");
                }
            }
            return storageCosts;
        }

        /// <summary>
        /// Reads the contents of a data file and creates a dictionary of storage costs for each tier.
        /// </summary>
        /// <param name="dataFilePath"></param>
        /// <returns></returns>
        private static Dictionary<StandardBlobTier, Models.StorageCosts> ReadStorageCostsFromDataFile(string dataFilePath)
        {
            try
            {
                var fileContents = File.ReadAllText(dataFilePath);
                JObject obj = JObject.Parse(fileContents);
                var hotTierStoragePricing = Models.StorageCosts.FromJson(obj["Costs"]["Hot"]);
                var coolTierStoragePricing = Models.StorageCosts.FromJson(obj["Costs"]["Cool"]);
                var archiveTierStoragePricing = Models.StorageCosts.FromJson(obj["Costs"]["Archive"]);
                return new Dictionary<StandardBlobTier, Models.StorageCosts>()
                {
                    { StandardBlobTier.Hot, hotTierStoragePricing },
                    { StandardBlobTier.Cool, coolTierStoragePricing },
                    { StandardBlobTier.Archive, archiveTierStoragePricing }
                };
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Reads the source type from command line arguments or 
        /// via user input.
        /// </summary>
        /// <returns>Source type.</returns>
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
                Console.WriteLine(new string('*', 80));
                Console.WriteLine("Enter the source type you want to analyze. Valid values are [L]ocal or [C]loud.");
                Console.WriteLine("To exit the application, enter \"X\"");
                Console.WriteLine(new string('*', 80));
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
        /// Reads the read % / month (% of blob reads / month) from command line arguments or 
        /// via user input.
        /// </summary>
        /// <returns>Source type.</returns>
        private static double GetReadPercentagePerMonthInput()
        {
            var readPercentagePerMonthInput = TryParseCommandLineArgumentsToExtractValue(ReadPercentagePerMonthArgumentName);
            double readPercentagePerMonth = 100;
            if (!string.IsNullOrWhiteSpace(readPercentagePerMonthInput))
            {
                var readPercentagePerMonthValue = readPercentagePerMonthInput.Remove(0, ReadPercentagePerMonthArgumentName.Length);
                if (double.TryParse(readPercentagePerMonthValue, out readPercentagePerMonth))
                {
                    if (readPercentagePerMonth >= 0) return readPercentagePerMonth;
                }
            }
            Console.WriteLine();
            Console.WriteLine(new string('*', 80));
            Console.WriteLine("Enter a numeric value indicating % of data that will be read from the storage account on a monthly basis.");
            Console.WriteLine("For example, if all blobs are read once a month, enter 100. If only 50% of the total capacity is read, enter 50.");
            Console.WriteLine("Press the \"Enter\" key for default value (100%).");
            Console.WriteLine("To exit the application, enter \"X\"");
            Console.WriteLine(new string('*', 80));
            Console.WriteLine();
            var consoleInput = Console.ReadLine().Trim().ToLowerInvariant();
            ExitApplicationIfRequired(consoleInput);
            if (consoleInput == "") return 100;
            consoleInput = consoleInput.Replace("%", "");
            if (!double.TryParse(consoleInput, out readPercentagePerMonth) || readPercentagePerMonth < 0)
            {
                Console.WriteLine("Invalid input for read percentage / month. Enter a valid numeric value greater than or equal to zero (0).");
                return GetReadPercentagePerMonthInput();
            }
            return readPercentagePerMonth;
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
                Console.WriteLine(new string('*', 80));
                Console.WriteLine("Enter the full path of a directory that you want to analyze.");
                Console.WriteLine("To exit the application, enter \"X\"");
                Console.WriteLine(new string('*', 80));
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
                Console.WriteLine(new string('*', 80));
                Console.WriteLine("Enter connection string for your storage account in the following format: DefaultEndpointsProtocol=https;AccountName=<youraccountname>;AccountKey=<youraccountkey>");
                Console.WriteLine("You can also enter an Account Shared Access Signature (SAS) URL");
                Console.WriteLine("To exit the application, enter \"X\"");
                Console.WriteLine(new string('*', 80));
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
                    var containerExistenceCheckResult = Helpers.BlobStorageHelper.DoesContainerExists(containerName).GetAwaiter().GetResult();
                    var containerExists = containerExistenceCheckResult.Item1;
                    var isValidConnectionString = containerExistenceCheckResult.Item2;
                    if (!isValidConnectionString)
                    {
                        Console.WriteLine("Unable to connect to storage account using the connection string provided. Please check the connection string and try again.");
                        ExitApplicationIfRequired("X");
                    }
                    if (!containerExists)
                    {
                        Console.WriteLine("Specified blob container does not exist in the storage account. Please specify a valid container name.");
                        return GetContainerInput();
                    }
                }
                return containerName;
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine(new string('*', 80));
                Console.WriteLine("Enter the blob container name that you want to analyze. To analyze all blob containers, enter \"*\".");
                Console.WriteLine("Press the \"Enter\" key for default value (*, all containers)");
                Console.WriteLine("To exit the application, enter \"X\"");
                Console.WriteLine(new string('*', 80));
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
                        var containerExistenceCheckResult = Helpers.BlobStorageHelper.DoesContainerExists(containerName).GetAwaiter().GetResult();
                        var containerExists = containerExistenceCheckResult.Item1;
                        var isValidConnectionString = containerExistenceCheckResult.Item2;
                        if (!isValidConnectionString)
                        {
                            Console.WriteLine("Unable to connect to storage account using the connection string provided. Please check the connection string and try again.");
                            ExitApplicationIfRequired("X");
                        }
                        if (!containerExists)
                        {
                            Console.WriteLine("Specified blob container does not exist in the storage account. Please specify a valid container name.");
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
            Console.WriteLine(new string('*', 80));
            Console.WriteLine("Enter the minimum last modified time (in days before the present time) of a blob / local file to be considered for analysis.");
            Console.WriteLine("If your blobs have never been modified, last modified time is equivalent to creation time.");
            Console.WriteLine("For example, specifying the value 30 will exclude all blobs created or modified in the last 30 days from analysis.");
            Console.WriteLine("Press the \"Enter\" key for default value (30 days).");
            Console.WriteLine("To exit the application, enter \"X\"");
            Console.WriteLine(new string('*', 80));
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
            Console.WriteLine(new string('*', 80));
            Console.WriteLine("Enter the minimum size of a blob / file (in bytes) to be considered for analysis.");
            Console.WriteLine("Press the \"Enter\" key for default value (0 bytes).");
            Console.WriteLine("To exit the application, enter \"X\"");
            Console.WriteLine(new string('*', 80));
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
                Console.WriteLine(new string('*', 80));
                Console.WriteLine("Enter the target tier for analysis. Valid values are \"A\" (archive tier), \"C\" (cool tier) or \"H\" (hot tier).");
                Console.WriteLine("To exit the application, enter \"X\"");
                Console.WriteLine(new string('*', 80));
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
        /// Reads the flag indicating if container level statistics should be shown via command line arguments.
        /// </summary>
        /// <returns></returns>
        private static bool GetShowContainerLevelStatistics()
        {
            bool showStatistics = false;
            var showContainerLevelStatisticsArgument = TryParseCommandLineArgumentsToExtractValue(ShowContainerLevelStatisticsArgumentName);
            if (!string.IsNullOrWhiteSpace(showContainerLevelStatisticsArgument))
            {
                var showContainerLevelStatisticsArgumentValue = showContainerLevelStatisticsArgument.Remove(0, ShowContainerLevelStatisticsArgumentName.Length);
                bool.TryParse(showContainerLevelStatisticsArgumentValue, out showStatistics);
            }
            return showStatistics;
        }

        private static Boolean GetAutoChangeTierInput(StandardBlobTier targetTier, string autoChangeTierArgument)
        {
            if (string.IsNullOrWhiteSpace(autoChangeTierArgument))
            {
                Console.WriteLine($"To change the access tier of the blobs to \"{targetTier.ToString()}\", enter \"Y\" now. Enter any other key to terminate the application.");
                Console.WriteLine("*Please be aware of the the one-time costs you will incur for changing the access tiers.");
                autoChangeTierArgument = Console.ReadLine().ToUpperInvariant();
            }
            if (!string.IsNullOrWhiteSpace(autoChangeTierArgument))
            {
                switch (autoChangeTierArgument)
                {
                    case "Y":
                        return true;
                    default:
                        return false;
                }
            }
            return false;
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

        private static void DoBlobsCostAnalysis(List<Models.ContainerStatistics> statistics, double readPercentagePerMonth)
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
            double currentReadsCosts = 0;
            long totalCount = 0;
            long totalSize = 0;
            double storageCostsAfterMove = 0.0;
            double dataTierChangeCost = 0.0;
            double dataRetrievalCost = 0.0;
            long totalMatchingBlobs = 0;

            Console.WriteLine($"Scenario: {scenarioText}");
            var header = string.Format("{0, 12}{1, 20}{2, 20}{3, 20}{4, 20}{5, 20}", "Access Tier", "Block Blobs Count", "Block Blobs Capacity", "Storage Costs/Month", "Read Costs/Month", "Total Costs/Month");
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
                if (storageCostSourceTier != null)
                {
                    foreach (var containerStatistics in statistics)
                    {
                        var matchingItem = containerStatistics.MatchingBlobsStatistics[sourceTier];
                        totalCountSourceTier += matchingItem.Count;
                        totalSizeSourceTier += matchingItem.Size;
                        currentStorageCostSourceTier += matchingItem.Size / Helpers.Constants.GB * storageCostSourceTier.DataStorageCostPerGB;
                    }
                    var readOperations = totalSizeSourceTier * readPercentagePerMonth / 100;
                    var averageBlobSize = totalCountSourceTier == 0 ? 0 : (double)totalSizeSourceTier / totalCountSourceTier;
                    var readTransactions = averageBlobSize == 0 ? 0 : readOperations / averageBlobSize;
                    var readsCostSourceTier = CalculateReadsCost(sourceTier, readTransactions, readOperations, storageCosts);
                    var readsPlusStorageCostSourceTier = currentStorageCostSourceTier + readsCostSourceTier.GetValueOrDefault();
                    Console.WriteLine("{0, 12}{1, 20}{2, 20}{3, 20}{4, 20}{5, 20}", sourceTier.ToString(), totalCountSourceTier, Helpers.Utils.SizeAsString(totalSizeSourceTier), currentStorageCostSourceTier.ToString("C"), readsCostSourceTier.HasValue ? readsCostSourceTier.Value.ToString("C") : "--", readsPlusStorageCostSourceTier.ToString("C"));
                    currentStorageCosts += currentStorageCostSourceTier;
                    currentReadsCosts += readsCostSourceTier.GetValueOrDefault();
                    totalMatchingBlobs += totalCountSourceTier;
                    totalCount += totalCountSourceTier;
                    totalSize += totalSizeSourceTier;
                    if ((sourceTier == StandardBlobTier.Hot && targetTier == StandardBlobTier.Cool) || (sourceTier == StandardBlobTier.Hot && targetTier == StandardBlobTier.Archive) || (sourceTier == StandardBlobTier.Cool && targetTier == StandardBlobTier.Archive))
                    {
                        if (storageCostTargetTier != null)
                        {
                            dataTierChangeCost += totalCountSourceTier * storageCostTargetTier.WriteOperationsCostPerTenThousand / 10000;
                            dataRetrievalCost += totalSizeSourceTier / Helpers.Constants.GB * storageCostTargetTier.DataWriteCostPerGB;
                        }
                    }
                    if ((sourceTier == StandardBlobTier.Archive && targetTier == StandardBlobTier.Hot) || (sourceTier == StandardBlobTier.Archive && targetTier == StandardBlobTier.Cool) || (sourceTier == StandardBlobTier.Cool && targetTier == StandardBlobTier.Hot))
                    {
                        dataTierChangeCost += totalCountSourceTier * storageCostSourceTier.ReadOperationsCostPerTenThousand / 10000;
                        dataRetrievalCost += totalSizeSourceTier / Helpers.Constants.GB * storageCostTargetTier.DataRetrievalCostPerGB;
                    }
                    storageCostsAfterMove += totalSizeSourceTier / Helpers.Constants.GB * (storageCostTargetTier == null ? 0 : storageCostTargetTier.DataStorageCostPerGB);
                }
                else
                {
                    Console.WriteLine("{0, 12}{1, 20}{2, 20}{3, 20}{4, 20}{5, 20}", sourceTier.ToString(), "--", "--", "--", "--", "--");
                }
            }
            long totalCountTargetTier = 0;
            long totalSizeTargetTier = 0;
            double currentStorageCostTargetTier = 0.0;
            if (storageCostTargetTier != null)
            {
                foreach (var containerStatistics in statistics)
                {
                    var matchingItem = containerStatistics.MatchingBlobsStatistics[targetTier];
                    totalCountTargetTier += matchingItem.Count;
                    totalSizeTargetTier += matchingItem.Size;
                    currentStorageCostTargetTier += matchingItem.Size / Helpers.Constants.GB * storageCostTargetTier.DataStorageCostPerGB;
                }
                var readOperations = totalSizeTargetTier * readPercentagePerMonth / 100;
                var averageBlobSize = totalCountTargetTier == 0 ? 0 : totalSizeTargetTier / totalCountTargetTier;
                var readTransactions = averageBlobSize == 0 ? 0 : readOperations / averageBlobSize;
                var readsCostTargetTier = CalculateReadsCost(targetTier, readTransactions, readOperations, storageCosts);
                var readsPlusStorageCostTargetTier = currentStorageCostTargetTier + readsCostTargetTier.GetValueOrDefault();
                currentStorageCosts += currentStorageCostTargetTier;
                currentReadsCosts += readsCostTargetTier.GetValueOrDefault();
                totalCount += totalCountTargetTier;
                totalSize += totalSizeTargetTier;
                Console.WriteLine("{0, 12}{1, 20}{2, 20}{3, 20}{4, 20}{5, 20}", targetTier.ToString(), totalCountTargetTier, Helpers.Utils.SizeAsString(totalSizeTargetTier), currentStorageCostTargetTier.ToString("C"), readsCostTargetTier.HasValue ? readsCostTargetTier.Value.ToString("C") : "--", readsPlusStorageCostTargetTier.ToString("C"));
            }
            else
            {
                Console.WriteLine("{0, 12}{1, 20}{2, 20}{3, 30}{4, 20}{5, 20}", targetTier.ToString(), "--", "--", "--", "--", "--");
            }
            Console.WriteLine(new string('-', header.Length));
            Console.WriteLine("{0, 12}{1, 20}{2, 20}{3, 20}{4, 20}{5, 20}", "Total", totalCount, Helpers.Utils.SizeAsString(totalSize), currentStorageCosts.ToString("C"), currentReadsCosts.ToString("C"), (currentStorageCosts + currentReadsCosts).ToString("C"));
            Console.WriteLine(new string('-', header.Length));
            Console.WriteLine();
            if (storageCostTargetTier != null)
            {
                var readOperations = totalSize * readPercentagePerMonth / 100;
                var averageBlobSize = totalCount == 0 ? 0 : (double)totalSize / totalCount;
                var readTransactions = averageBlobSize == 0 ? 0 : readOperations / averageBlobSize;
                var readsCostAfterMigration = CalculateReadsCost(targetTier, readTransactions, readOperations, storageCosts);
                var totalCostAfterMigration = storageCostsAfterMove + readsCostAfterMigration.GetValueOrDefault();
                Console.WriteLine("Storage Costs After Migration:");
                Console.WriteLine(new string('-', header.Length));
                Console.WriteLine(header);
                Console.WriteLine(new string('-', header.Length));
                foreach (var sourceTier in sourceTiers)
                {
                    Console.WriteLine("{0, 12}{1, 20}{2, 20}{3, 20}{4, 20}{5, 20}", sourceTier.ToString(), 0, Helpers.Utils.SizeAsString(0), 0.ToString("C"), 0.ToString("C"), 0.ToString("C"));
                }
                Console.WriteLine("{0, 12}{1, 20}{2, 20}{3, 20}{4, 20}{5, 20}", targetTier.ToString(), totalCount, Helpers.Utils.SizeAsString(totalSize), storageCostsAfterMove.ToString("C"), readsCostAfterMigration.HasValue ? readsCostAfterMigration.Value.ToString("C") : "--", totalCostAfterMigration.ToString("C"));
                Console.WriteLine(new string('-', header.Length));
                Console.WriteLine("{0, 12}{1, 20}{2, 20}{3, 20}{4, 20}{5, 20}", "Total", totalCount, Helpers.Utils.SizeAsString(totalSize), storageCostsAfterMove.ToString("C"), readsCostAfterMigration.HasValue ? readsCostAfterMigration.Value.ToString("C") : "--", totalCostAfterMigration.ToString("C"));
                var netSavingsStorageCosts = currentStorageCosts - storageCostsAfterMove;
                var netSavingsReadCosts = currentReadsCosts - readsCostAfterMigration.GetValueOrDefault();
                var netSavingsStoragePlusReadCosts = netSavingsStorageCosts + netSavingsReadCosts;
                Console.WriteLine("{0, 12}{1, 20}{2, 20}{3, 20}{4, 20}{5, 20}", "Savings", "--", "--",
                                  (netSavingsStorageCosts >= 0 ? "" : "-") + Math.Abs(netSavingsStorageCosts).ToString("C"),
                                  (netSavingsReadCosts >= 0 ? "" : "-") + Math.Abs(netSavingsReadCosts).ToString("C"),
                                  (netSavingsStoragePlusReadCosts >= 0 ? "" : "-") + Math.Abs(netSavingsStoragePlusReadCosts).ToString("C"));
                Console.WriteLine(new string('-', header.Length));
                Console.WriteLine("{0, 62}{1, 20}", "One time cost of data retrieval and changing blob access tier:", (dataTierChangeCost + dataRetrievalCost).ToString("C"));
                var costSavings = currentStorageCosts + currentReadsCosts - totalCostAfterMigration;// + dataTierChangeCost + dataRetrievalCost;
                Console.WriteLine("{0, 62}{1, 20}", "Net Savings (Per Month):", (costSavings >= 0 ? "" : "-") + Math.Abs(costSavings).ToString("C"));
                Console.WriteLine(new string('-', header.Length));
                Console.WriteLine();
                Console.WriteLine("*All currency values are rounded to the nearest cent and are in US Dollars ($).");
                Console.WriteLine();
                Console.WriteLine("*Read costs does not include data egress costs.");
                Console.WriteLine();
                Console.WriteLine("*Storage costs do not include metadata charges.");
                Console.WriteLine();
                Console.WriteLine("*For \"Archive\" tier, preview prices are being used. Please note that it may take up to 15 hours to read a blob from this tier.");
                Console.WriteLine();
                var autoChangeTierInput = TryParseCommandLineArgumentsToExtractValue(AutoChangeTierArgumentName);
                if (!String.IsNullOrWhiteSpace(autoChangeTierInput))
                {
                    autoChangeTierInput = autoChangeTierInput.Remove(0, AutoChangeTierArgumentName.Length);
                }
                var autoChangeTier = GetAutoChangeTierInput(targetTier, autoChangeTierInput);
                if (autoChangeTier)
                {
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
                        Console.WriteLine($"Successully moved {successCount} block blobs to \"{targetTier.ToString()}\" access tier.");
                    }
                    ExitApplicationIfRequired("X");
                }
                else
                {
                    ExitApplicationIfRequired("X");
                }
            }
            else
            {
                Console.WriteLine($"Either \"{targetTier.ToString()}\" access tier is not available for the selected region or pricing is not defined for \"{targetTier.ToString()}\" access tier in the region file.");
                ExitApplicationIfRequired("X");
            }
        }

        private static double? CalculateReadsCost(StandardBlobTier sourceTier, double readTransactions, double readOperations, Dictionary<StandardBlobTier, Models.StorageCosts> costs)
        {
            double? readCostsPerMonth = null;
            var storageCostsHotAccessTier = costs[StandardBlobTier.Hot];
            var storageCostsCoolAccessTier = costs[StandardBlobTier.Cool];
            var storageCostsArchiveAccessTier = costs[StandardBlobTier.Archive];
            switch (sourceTier)
            {
                case StandardBlobTier.Hot:
                    {
                        if (storageCostsHotAccessTier != null)
                        {
                            readCostsPerMonth = storageCostsHotAccessTier.ReadOperationsCostPerTenThousand * readTransactions / 10000 +
                                    storageCostsHotAccessTier.DataRetrievalCostPerGB * readOperations / Helpers.Constants.GB; ;
                        }
                        break;
                    }
                case StandardBlobTier.Cool:
                    {
                        if (storageCostsCoolAccessTier != null)
                        {
                            readCostsPerMonth = storageCostsCoolAccessTier.ReadOperationsCostPerTenThousand * readTransactions / 10000 +
                                    storageCostsCoolAccessTier.DataRetrievalCostPerGB * readOperations / Helpers.Constants.GB;
                        }
                        break;
                    }
                case StandardBlobTier.Archive:
                    {
                        if (storageCostsArchiveAccessTier != null)
                        {
                            //For calculation of read costs in "Archive" access tier, this is the formula used:
                            //1. First read blobs from archive tier (data retrieval cost) = Size of blobs read (in GB) x data retrieval cost (per GB).
                            //2. Convert the blob tier to hot = # of blobs read x data tier change cost (archive) / 10000;
                            //3. Blobs read cost from hot tier = # of blobs read x read transaction cost (hot tier) / 10000.
                            //4. Convert the blob tier back to archive = # of blobs read x data tier change cost (archive) / 10000.
                            //Blobs read cost = 1 + 2 + 3 + 4.
                            var dataRetrievalCostArchiveTier = storageCostsArchiveAccessTier.DataRetrievalCostPerGB * readOperations / Helpers.Constants.GB;
                            var dataTierChangeFromArchiveToHotCost = storageCostsArchiveAccessTier.ReadOperationsCostPerTenThousand * readTransactions / 10000;
                            var dataReadTransactionsHotTierCost = storageCostsHotAccessTier == null ? 0 : storageCostsHotAccessTier.ReadOperationsCostPerTenThousand * readTransactions / 10000;
                            var dataTierChangeFromHotToArchiveCost = storageCostsArchiveAccessTier.WriteOperationsCostPerTenThousand * readTransactions / 10000;
                            readCostsPerMonth = dataRetrievalCostArchiveTier + dataTierChangeFromArchiveToHotCost + dataReadTransactionsHotTierCost + dataTierChangeFromHotToArchiveCost;
                        }
                        break;
                    }
            }
            return readCostsPerMonth;
        }
    }
}
