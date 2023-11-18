// delete when uploading to Github

using ThermoFisher.CommonCore.Data.FilterEnums;

namespace MultiMSIProcessor
{
    class ScanIndex
    {
        public Dictionary<int, ScanData> allScans;
        public MSOrderType AnalysisOrder;
        public Dictionary<MSOrderType, int[]> ScanEnumerators;
        public int TotalScans;

        public ScanIndex()
        {
            ScanEnumerators = new Dictionary<MSOrderType, int[]>();
        }
    }
    class ScanData
    {
        public MSOrderType MSOrder;
        public MassAnalyzerType MassAnalyzer;
        public bool HasPrecursors;
        public bool HasDependents;
        public double CompensationVoltage;
    }
}
