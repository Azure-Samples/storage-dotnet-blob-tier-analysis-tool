﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Text.Json;

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

        public static StorageCosts FromJson(JsonElement json)
        {
            if (json.Equals(null)) return null;
            try
            {
                double dataStorageCostPerGB = 0;
                double writeOperationsCostPerTenThousand = 0;
                double readOperationsCostPerTenThousand = 0;
                double dataRetrievalCostPerGB = 0;
                double dataWriteCostPerGB = 0;
                JsonElement token = json.GetProperty("DataStorageCostPerGB");
                double.TryParse(token.ToString(), out dataStorageCostPerGB);
                token = json.GetProperty("WriteOperationsCostPerTenThousand");
                double.TryParse(token.ToString(), out writeOperationsCostPerTenThousand);
                token = json.GetProperty("ReadOperationsCostPerTenThousand");
                double.TryParse(token.ToString(), out readOperationsCostPerTenThousand);
                token = json.GetProperty("DataRetrievalCostPerGB");
                double.TryParse(token.ToString(), out dataRetrievalCostPerGB);
                token = json.GetProperty("DataWriteCostPerGB");
                double.TryParse(token.ToString(), out dataWriteCostPerGB);
                return new StorageCosts(dataStorageCostPerGB, writeOperationsCostPerTenThousand, readOperationsCostPerTenThousand, dataRetrievalCostPerGB, dataWriteCostPerGB);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
