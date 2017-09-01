namespace BlobTierAnalysisTool.Models
{
    public class StorageCosts
    {
        private readonly double _dataStorageCostPerGB;

        private readonly double _writeOperationsCostPerTenThousand;

        private readonly double _readOperationsCostPerTenThousand;

        private readonly double _dataRetrievalCostPerGB;

        private readonly double _dataWriteCostPerGB;

        public StorageCosts(double dataStorageCostPerGB, double writeOperationsCostPerTenThousand, double readOperationsCostPerTenThousand, double dataRetrievalCostPerGB, double dataWriteCostPerGB)
        {
            _dataStorageCostPerGB = dataStorageCostPerGB;
            _writeOperationsCostPerTenThousand = writeOperationsCostPerTenThousand;
            _readOperationsCostPerTenThousand = readOperationsCostPerTenThousand;
            _dataRetrievalCostPerGB = dataRetrievalCostPerGB;
            _dataWriteCostPerGB = dataWriteCostPerGB;
        }

        public double DataStorageCostPerGB { get { return _dataStorageCostPerGB; } }

        public double WriteOperationsCostPerTenThousand { get { return _writeOperationsCostPerTenThousand; } }

        public double ReadOperationsCostPerTenThousand { get { return _readOperationsCostPerTenThousand; } }

        public double DataRetrievalCostPerGB { get { return _dataRetrievalCostPerGB; } }

        public double DataWriteCostPerGB { get { return _dataWriteCostPerGB; } }
    }
}
