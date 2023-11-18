// -------------------------------------------------------------------------------
// Written by Matthew Monroe for the Department of Energy (PNNL, Richland, WA)
// Copyright 2021, Battelle Memorial Institute.  All Rights Reserved.
//
// E-mail: matthew.monroe@pnl.gov or proteomics@pnnl.gov
// Website: https://github.com/PNNL-Comp-Mass-Spec/ or https://panomics.pnnl.gov/ or https://www.pnnl.gov/integrative-omics
// -------------------------------------------------------------------------------

using System;
using System.Xml;

namespace MSDataFileReader
{
    /// <summary>
    /// This class uses a SAX Parser to read an mzXML file
    /// </summary>
    public class MzXMLFileReader : MsXMLFileReaderBaseClass
    {
        // Ignore Spelling: centroided, msconvert, num, xmlns, xsi, zlib

        /// <summary>
        /// Constructor
        /// </summary>
        public MzXMLFileReader()
        {
            InitializeLocalVariables();
        }

        /// <summary>
        /// MzXML file extension
        /// </summary>
        /// <remarks>
        /// Must be in all caps
        /// </remarks>
        public const string MZXML_FILE_EXTENSION = ".MZXML";

        /// <summary>
        /// Alternative MzXML file extension
        /// </summary>
        /// <remarks>
        /// Must be in all caps
        /// </remarks>
        public const string MZXML_FILE_EXTENSION_XML = "_MZXML.XML";

        private static class XMLSectionNames
        {
            public const string RootName = "mzXML";

            public const string MsRun = "msRun";
        }

        private static class MzXMLRootAttributeNames
        {
            public const string Xmlns = "xmlns";

            public const string XsiSchemaLocation = "xsi:schemaLocation";
        }

        private static class HeaderSectionNames
        {
            public const string MsInstrument = "msInstrument";

            public const string DataProcessing = "dataProcessing";
        }

        private static class ScanSectionNames
        {
            public const string Scan = "scan";

            public const string PrecursorMz = "precursorMz";

            public const string Peaks = "peaks";
        }

        private static class MSRunAttributeNames
        {
            public const string ScanCount = "scanCount";

            public const string StartTime = "startTime";

            public const string EndTime = "endTime";
        }

        private static class DataProcessingAttributeNames
        {
            public const string Centroided = "centroided";
        }

        private static class ScanAttributeNames
        {
            public const string ScanNumber = "num";

            public const string MsLevel = "msLevel";

            /// <summary>
            /// 0 means not centroided, 1 means centroided
            /// </summary>
            public const string Centroided = "centroided";

            public const string PeaksCount = "peaksCount";

            public const string Polarity = "polarity";

            /// <summary>
            /// Scan type: Full, zoom, SIM, SRM, MRM, CRM, Q1, or Q3
            /// </summary>
            /// <remarks>
            /// MRM and SRM and functionally equivalent; ReAdW uses SRM
            /// </remarks>
            public const string ScanType = "scanType";

            /// <summary>
            /// Thermo-specific filter-line text; added by ReAdW
            /// </summary>
            public const string FilterLine = "filterLine";

            /// <summary>
            /// Retention time
            /// </summary>
            /// <remarks>
            /// Example: PT1.0373S
            /// </remarks>
            public const string RetentionTime = "retentionTime";

            /// <summary>
            /// Collision energy used to fragment the parent ion
            /// </summary>
            public const string CollisionEnergy = "collisionEnergy";

            /// <summary>
            /// Low m/z boundary (this is the instrumental setting)
            /// </summary>
            /// <remarks>
            /// Not present in .mzXML files created with ReAdW
            /// </remarks>
            public const string StartMz = "startMz";

            /// <summary>
            /// High m/z boundary (this is the instrumental setting)
            /// </summary>
            /// <remarks>
            /// Not present in .mzXML files created with ReAdW
            /// </remarks>
            public const string EndMz = "endMz";

            /// <summary>
            /// Observed low m/z (this is the m/z of the first ion observed)
            /// </summary>
            public const string LowMz = "lowMz";

            /// <summary>
            /// Observed high m/z (this is the m/z of the last ion observed)
            /// </summary>
            public const string HighMz = "highMz";

            /// <summary>
            /// m/z of the base peak (most intense peak)
            /// </summary>
            public const string BasePeakMz = "basePeakMz";

            /// <summary>
            /// Intensity of the base peak (most intense peak)
            /// </summary>
            public const string BasePeakIntensity = "basePeakIntensity";

            /// <summary>
            /// Total ion current (total intensity in the scan)
            /// </summary>
            public const string TotalIonCurrent = "totIonCurrent";

            public const string MsInstrumentID = "msInstrumentID";
        }

        private static class PrecursorAttributeNames
        {
            /// <summary>
            /// Scan number of the precursor
            /// </summary>
            public const string PrecursorScanNum = "precursorScanNum";

            /// <summary>
            /// Intensity of the precursor ion
            /// </summary>
            public const string PrecursorIntensity = "precursorIntensity";

            /// <summary>
            /// Charge of the precursor, typically determined at time of acquisition by the mass spectrometer
            /// </summary>
            public const string PrecursorCharge = "precursorCharge";

            /// <summary>
            /// Fragmentation method, e.g. CID, ETD, or HCD
            /// </summary>
            public const string ActivationMethod = "activationMethod";

            /// <summary>
            /// Isolation window width, e.g. 2.0
            /// </summary>
            public const string WindowWideness = "windowWideness";
        }

        private static class PeaksAttributeNames
        {
            public const string Precision = "precision";

            public const string ByteOrder = "byteOrder";

            /// <summary>
            /// Pair order, for example: "m/z-int"
            /// </summary>
            /// <remarks>
            /// Superseded by "contentType" in mzXML 3
            /// </remarks>
            public const string PairOrder = "pairOrder";

            /// <summary>
            /// Compression type: "none" or "zlib"
            /// </summary>
            public const string CompressionType = "compressionType";

            /// <summary>
            /// Integer value required when using zlib compression
            /// </summary>
            public const string CompressedLength = "compressedLen";

            /// <summary>
            /// Content type: "m/z-int", "m/z", "intensity", "S/N", "charge", "m/z ruler", or "TOF"
            /// </summary>
            public const string ContentType = "contentType";
        }

        private enum CurrentMzXMLDataFileSection
        {
            UnknownFile = 0,
            Start = 1,
            msRun = 2,
            msInstrument = 3,
            dataProcessing = 4,
            ScanList = 5
        }

        private struct FileStatsAddnlType
        {
            public float StartTimeMin;

            public float EndTimeMin;

            /// <summary>
            /// True if centroid (aka stick) data
            /// False if profile (aka continuum) data
            /// </summary>
            public bool IsCentroid;
        }

        private CurrentMzXMLDataFileSection mCurrentXMLDataFileSection;

        /// <summary>
        /// Scan depth
        /// </summary>
        /// <remarks>
        /// Greater than 0 if inside a scan element
        /// </remarks>
        private int mScanDepth;

        private SpectrumInfoMzXML mCurrentSpectrum;

        private FileStatsAddnlType mInputFileStatsAddnl;

        // ReSharper disable UnusedMember.Global

        public float FileInfoStartTimeMin => mInputFileStatsAddnl.StartTimeMin;

        public float FileInfoEndTimeMin => mInputFileStatsAddnl.EndTimeMin;

        public bool FileInfoIsCentroid => mInputFileStatsAddnl.IsCentroid;

        // ReSharper restore UnusedMember.Global

        protected override SpectrumInfo GetCurrentSpectrum()
        {
            return mCurrentSpectrum;
        }

        protected override void InitializeCurrentSpectrum()
        {
            mCurrentSpectrum = new SpectrumInfoMzXML();
        }

        protected sealed override void InitializeLocalVariables()
        {
            base.InitializeLocalVariables();
            mCurrentXMLDataFileSection = CurrentMzXMLDataFileSection.UnknownFile;
            mScanDepth = 0;

            mInputFileStatsAddnl.StartTimeMin = 0f;
            mInputFileStatsAddnl.EndTimeMin = 0f;
            mInputFileStatsAddnl.IsCentroid = false;
        }

        public override bool OpenFile(string inputFilePath)
        {
            InitializeLocalVariables();
            return base.OpenFile(inputFilePath);
        }

        /// <summary>
        /// Parse msmsDataBase64Encoded and store the data in mIntensityList() and mMZList()
        /// </summary>
        /// <param name="msmsDataBase64Encoded"></param>
        /// <param name="compressionType"></param>
        /// <returns>True if successful, false if an error</returns>
        private void ParseBinaryData(string msmsDataBase64Encoded, string compressionType)
        {
            const Base64EncodeDecode.EndianType endianMode = Base64EncodeDecode.EndianType.BigEndian;

            if (mCurrentSpectrum is null)
            {
                return;
            }

            mCurrentSpectrum.ClearMzAndIntensityData();

            if (string.IsNullOrEmpty(msmsDataBase64Encoded))
            {
                return;
            }

            try
            {
                var zLibCompressed = compressionType.Equals(SpectrumInfoMzXML.CompressionTypes.ZLib);

                var success = false;

                switch (mCurrentSpectrum.NumericPrecisionOfData)
                {
                    case 32:
                        if (Base64EncodeDecode.DecodeNumericArray(msmsDataBase64Encoded, out float[] floatArray, zLibCompressed, endianMode))
                        {
                            // floatArray now contains pairs of singles, either m/z and intensity or intensity and m/z
                            // Need to split this apart into two arrays

                            if (mCurrentSpectrum.PeaksPairOrder.Equals(SpectrumInfoMzXML.PairOrderTypes.IntensityAndMz))
                            {
                                var indexEnd = floatArray.Length - 1;

                                for (var index = 0; index <= indexEnd; index += 2)
                                {
                                    // index + 1 has m/z
                                    // index has intensity
                                    mCurrentSpectrum.StoreIon(floatArray[index + 1], floatArray[index]);
                                }
                            }
                            else
                            {
                                // Assume PairOrderTypes.MzAndIntensity
                                var indexEnd = floatArray.Length - 1;

                                for (var index = 0; index <= indexEnd; index += 2)
                                {
                                    // index has m/z
                                    // index + 1 has intensity
                                    mCurrentSpectrum.StoreIon(floatArray[index], floatArray[index + 1]);
                                }
                            }

                            success = true;
                        }

                        break;

                    case 64:
                        if (Base64EncodeDecode.DecodeNumericArray(msmsDataBase64Encoded, out double[] doubleArray, zLibCompressed, endianMode))
                        {
                            // doubleArray now contains pairs of doubles, either m/z and intensity or intensity and m/z
                            // Need to split this apart into two arrays

                            if (mCurrentSpectrum.PeaksPairOrder.Equals(SpectrumInfoMzXML.PairOrderTypes.IntensityAndMz))
                            {
                                var indexEnd = doubleArray.Length - 1;

                                for (var index = 0; index <= indexEnd; index += 2)
                                {
                                    // index + 1 has m/z
                                    // index has intensity
                                    mCurrentSpectrum.StoreIon(doubleArray[index + 1], (float)doubleArray[index]);
                                }
                            }
                            else
                            {
                                // Assume PairOrderTypes.MzAndIntensity
                                var indexEnd = doubleArray.Length - 1;

                                for (var index = 0; index <= indexEnd; index += 2)
                                {
                                    // index has m/z
                                    // index + 1 has intensity
                                    mCurrentSpectrum.StoreIon(doubleArray[index], (float)doubleArray[index + 1]);
                                }
                            }

                            success = true;
                        }

                        break;

                    default:
                        // Invalid numeric precision
                        break;
                }

                if (!success)
                    return;

                if (mCurrentSpectrum.MzList.Count > 1)
                {
                    // Check whether the last entry has a mass and intensity of 0
                    if (Math.Abs(mCurrentSpectrum.MzList[mCurrentSpectrum.MzList.Count - 1]) < float.Epsilon &&
                        Math.Abs(mCurrentSpectrum.IntensityList[mCurrentSpectrum.MzList.Count - 1]) < float.Epsilon)
                    {
                        // Remove the final entry
                        var targetIndex = mCurrentSpectrum.MzList.Count - 1;
                        mCurrentSpectrum.MzList.RemoveAt(targetIndex);
                        mCurrentSpectrum.IntensityList.RemoveAt(targetIndex);
                    }
                }
            }
            catch (Exception ex)
            {
                OnErrorEvent("Error in ParseBinaryData", ex);
            }
        }

        protected override void ParseElementContent()
        {
            if (mAbortProcessing)
                return;

            if (mCurrentSpectrum is null)
                return;

            try
            {
                // Skip the element if we aren't parsing a scan (inside a scan element)
                // This is an easy way to skip whitespace
                // We can do this since we only care about the data inside the
                // ScanSectionNames.PrecursorMz and ScanSectionNames.Peaks elements

                if (mScanDepth > 0)
                {
                    // Check the last element name sent to startElement to determine
                    // what to do with the data we just received
                    switch (mCurrentElement)
                    {
                        case ScanSectionNames.PrecursorMz:
                            try
                            {
                                mCurrentSpectrum.ParentIonMZ = double.Parse(XMLTextReaderGetInnerText());
                            }
                            catch (Exception ex)
                            {
                                mCurrentSpectrum.ParentIonMZ = 0d;
                            }

                            break;

                        case ScanSectionNames.Peaks:
                            if (!mSkipBinaryData)
                            {
                                ParseBinaryData(XMLTextReaderGetInnerText(), mCurrentSpectrum.CompressionType);
                            }

                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                OnErrorEvent("Error in ParseElementContent", ex);
            }
        }

        protected override void ParseEndElement()
        {
            if (mAbortProcessing)
                return;

            if (mCurrentSpectrum is null)
                return;

            try
            {
                // If we just moved out of a scan element, finalize the current scan
                if (mXMLReader.Name == ScanSectionNames.Scan)
                {
                    if (mCurrentSpectrum.SpectrumStatus != SpectrumInfo.SpectrumStatusMode.Initialized && mCurrentSpectrum.SpectrumStatus != SpectrumInfo.SpectrumStatusMode.Validated)
                    {
                        mCurrentSpectrum.Validate();
                        mSpectrumFound = true;
                    }

                    mScanDepth--;

                    if (mScanDepth < 0)
                    {
                        // This shouldn't happen
                        OnErrorEvent("Unexpected condition in ParseEndElement: mScanDepth < 0");
                        mScanDepth = 0;
                    }
                }

                ParentElementStackRemove();

                // Clear the current element name
                mCurrentElement = string.Empty;
            }
            catch (Exception ex)
            {
                OnErrorEvent("Error in ParseEndElement", ex);
            }
        }

        protected override void ParseStartElement()
        {
            if (mAbortProcessing)
                return;

            if (mCurrentSpectrum is null)
                return;

            if (!mSkippedStartElementAdvance)
            {
                // Add mXMLReader.Name to mParentElementStack
                ParentElementStackAdd(mXMLReader);
            }

            // Store name of the element we just entered
            mCurrentElement = mXMLReader.Name;

            switch (mXMLReader.Name)
            {
                case ScanSectionNames.Scan:
                    mCurrentXMLDataFileSection = CurrentMzXMLDataFileSection.ScanList;

                    if (mScanDepth > 0 && !mSkippedStartElementAdvance)
                    {
                        if (mCurrentSpectrum.SpectrumStatus != SpectrumInfo.SpectrumStatusMode.Initialized && mCurrentSpectrum.SpectrumStatus != SpectrumInfo.SpectrumStatusMode.Validated)
                        {
                            mCurrentSpectrum.Validate();
                            mSkipNextReaderAdvance = true;
                            mSpectrumFound = true;
                            return;
                        }
                    }

                    mCurrentSpectrum.Clear();
                    mScanDepth++;
                    bool attributeMissing;

                    if (!mXMLReader.HasAttributes)
                    {
                        attributeMissing = true;
                    }
                    else
                    {
                        mCurrentSpectrum.ScanNumber = GetAttribValue(ScanAttributeNames.ScanNumber, int.MinValue);

                        if (mCurrentSpectrum.ScanNumber == int.MinValue)
                        {
                            attributeMissing = true;
                        }
                        else
                        {
                            attributeMissing = false;

                            mCurrentSpectrum.ScanCount = 1;
                            mCurrentSpectrum.ScanNumberEnd = mCurrentSpectrum.ScanNumber;
                            mCurrentSpectrum.MSLevel = GetAttribValue(ScanAttributeNames.MsLevel, 1);

                            mCurrentSpectrum.Centroided = GetAttribValue(ScanAttributeNames.Centroided, 0) != 0;

                            // ReSharper disable once UnusedVariable
                            var instrumentID = GetAttribValue(ScanAttributeNames.MsInstrumentID, 1);

                            // Store the expected data count
                            // This will be updated if the actual data is loaded
                            mCurrentSpectrum.PeaksCount = GetAttribValue(ScanAttributeNames.PeaksCount, 0);
                            mCurrentSpectrum.Polarity = GetAttribValue(ScanAttributeNames.Polarity, "+");
                            mCurrentSpectrum.RetentionTimeMin = GetAttribTimeValueMinutes(ScanAttributeNames.RetentionTime);
                            mCurrentSpectrum.CollisionEnergy = GetAttribValue(ScanAttributeNames.CollisionEnergy, 0F);
                            mCurrentSpectrum.ScanType = GetAttribValue(ScanAttributeNames.ScanType, string.Empty);

                            // ReAdW includes the filter text in .mzXML files; msconvert does not
                            mCurrentSpectrum.FilterLine = GetAttribValue(ScanAttributeNames.FilterLine, string.Empty);

                            mCurrentSpectrum.StartMZ = GetAttribValue(ScanAttributeNames.StartMz, 0F);
                            mCurrentSpectrum.EndMZ = GetAttribValue(ScanAttributeNames.EndMz, 0F);

                            mCurrentSpectrum.MzRangeStart = GetAttribValue(ScanAttributeNames.LowMz, 0F);
                            mCurrentSpectrum.MzRangeEnd = GetAttribValue(ScanAttributeNames.HighMz, 0F);

                            mCurrentSpectrum.BasePeakMZ = GetAttribValue(ScanAttributeNames.BasePeakMz, 0.0);
                            mCurrentSpectrum.BasePeakIntensity = GetAttribValue(ScanAttributeNames.BasePeakIntensity, 0F);
                            mCurrentSpectrum.TotalIonCurrent = GetAttribValue(ScanAttributeNames.TotalIonCurrent, 0.0);
                        }
                    }

                    if (attributeMissing)
                    {
                        mCurrentSpectrum.ScanNumber = 0;
                        OnErrorEvent("Unable to read the 'num' attribute for the current scan since it is missing");
                    }

                    break;

                case ScanSectionNames.PrecursorMz:
                    if (mXMLReader.HasAttributes)
                    {
                        mCurrentSpectrum.ParentIonIntensity = GetAttribValue(PrecursorAttributeNames.PrecursorIntensity, 0);
                        mCurrentSpectrum.ActivationMethod = GetAttribValue(PrecursorAttributeNames.ActivationMethod, string.Empty);
                        mCurrentSpectrum.ParentIonCharge = GetAttribValue(PrecursorAttributeNames.PrecursorCharge, 0);
                        mCurrentSpectrum.PrecursorScanNum = GetAttribValue(PrecursorAttributeNames.PrecursorScanNum, 0);
                        mCurrentSpectrum.IsolationWindow = GetAttribValue(PrecursorAttributeNames.WindowWideness, 0);
                    }

                    break;

                case ScanSectionNames.Peaks:
                    if (mXMLReader.HasAttributes)
                    {
                        // mzXML 3.x files will have a contentType attribute
                        // Earlier versions will have a pairOrder attribute

                        mCurrentSpectrum.PeaksPairOrder = GetAttribValue(PeaksAttributeNames.ContentType, string.Empty);

                        if (!string.IsNullOrEmpty(mCurrentSpectrum.PeaksPairOrder))
                        {
                            // mzXML v3.x
                            mCurrentSpectrum.CompressionType = GetAttribValue(PeaksAttributeNames.CompressionType, SpectrumInfoMzXML.CompressionTypes.None);

                            mCurrentSpectrum.CompressedLen = GetAttribValue(PeaksAttributeNames.CompressedLength, 0);
                        }
                        else
                        {
                            // mzXML v1.x or v2.x
                            mCurrentSpectrum.PeaksPairOrder = GetAttribValue(PeaksAttributeNames.PairOrder, SpectrumInfoMzXML.PairOrderTypes.MzAndIntensity);

                            mCurrentSpectrum.CompressionType = SpectrumInfoMzXML.CompressionTypes.None;
                            mCurrentSpectrum.CompressedLen = 0;
                        }

                        mCurrentSpectrum.NumericPrecisionOfData = GetAttribValue(PeaksAttributeNames.Precision, 32);
                        mCurrentSpectrum.PeaksByteOrder = GetAttribValue(PeaksAttributeNames.ByteOrder, SpectrumInfoMzXML.ByteOrderTypes.Network);
                    }

                    break;

                case XMLSectionNames.RootName:
                    mCurrentXMLDataFileSection = CurrentMzXMLDataFileSection.Start;

                    if (mXMLReader.HasAttributes)
                    {
                        // First look for attribute xmlns
                        var value = GetAttribValue(MzXMLRootAttributeNames.Xmlns, string.Empty);

                        if (string.IsNullOrEmpty(value))
                        {
                            // Attribute not found; look for attribute xsi:schemaLocation
                            value = GetAttribValue(MzXMLRootAttributeNames.XsiSchemaLocation, string.Empty);
                        }

                        ValidateMZXmlFileVersion(value);
                    }

                    break;

                case HeaderSectionNames.MsInstrument:
                    if (GetParentElement().Equals(XMLSectionNames.MsRun))
                    {
                        mCurrentXMLDataFileSection = CurrentMzXMLDataFileSection.msInstrument;
                    }

                    break;

                case XMLSectionNames.MsRun:
                    mCurrentXMLDataFileSection = CurrentMzXMLDataFileSection.msRun;

                    if (mXMLReader.HasAttributes)
                    {
                        mInputFileStats.ScanCount = GetAttribValue(MSRunAttributeNames.ScanCount, 0);
                        mInputFileStatsAddnl.StartTimeMin = GetAttribTimeValueMinutes(MSRunAttributeNames.StartTime);
                        mInputFileStatsAddnl.EndTimeMin = GetAttribTimeValueMinutes(MSRunAttributeNames.EndTime);

                        // Note: The ReAdW software that creates mzXML files records the .StartTime and .EndTime values in minutes but labels them as seconds
                        // Check for this by computing the average seconds/scan
                        // If too low, multiply the start and end times by 60
                        if (mInputFileStats.ScanCount > 0)
                        {
                            if ((mInputFileStatsAddnl.EndTimeMin - mInputFileStatsAddnl.StartTimeMin) / mInputFileStats.ScanCount * 60f < 0.1d)
                            {
                                // Less than 0.1 sec/scan; this is unlikely
                                mInputFileStatsAddnl.StartTimeMin *= 60f;
                                mInputFileStatsAddnl.EndTimeMin *= 60f;
                            }
                        }
                    }
                    else
                    {
                        mInputFileStats.ScanCount = 0;
                    }

                    break;

                case HeaderSectionNames.DataProcessing:
                    if (GetParentElement().Equals(XMLSectionNames.MsRun))
                    {
                        mCurrentXMLDataFileSection = CurrentMzXMLDataFileSection.dataProcessing;
                        mInputFileStatsAddnl.IsCentroid = GetAttribValue(DataProcessingAttributeNames.Centroided, false);
                    }

                    break;
            }

            mSkippedStartElementAdvance = false;
        }

        /// <summary>
        /// Updates the current XMLReader object with a new reader positioned at the XML for a new mass spectrum
        /// </summary>
        /// <param name="newReader"></param>
        /// <returns>True if successful, false if an error</returns>
        public bool SetXMLReaderForSpectrum(XmlReader newReader)
        {
            try
            {
                mInputFilePath = "TextStream";
                mXMLReader = newReader;
                mErrorMessage = string.Empty;
                InitializeLocalVariables();
                return true;
            }
            catch (Exception ex)
            {
                mErrorMessage = "Error updating mXMLReader";
                OnErrorEvent(mErrorMessage, ex);
                return false;
            }
        }

        /// <summary>
        /// Determine the .mzXML file version
        /// </summary>
        /// <remarks>
        /// The supported versions are mzXML_2.x and mzXML_3.x
        /// </remarks>
        /// <param name="xmlWithFileVersion"></param>
        /// <param name="xmlFileVersion">Output: file version</param>
        /// <returns>True if the version could be determined, otherwise false</returns>
        public static bool ExtractMzXmlFileVersion(string xmlWithFileVersion, out string xmlFileVersion)
        {
            var fileVersionRegEx = new System.Text.RegularExpressions.Regex(@"mzXML_[^\s""/]+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // Validate the mzXML file version
            if (!string.IsNullOrWhiteSpace(xmlWithFileVersion))
            {
                // Parse out the version number
                var match = fileVersionRegEx.Match(xmlWithFileVersion);

                if (match.Success)
                {
                    // Record the version
                    xmlFileVersion = match.Value;
                    return true;
                }
            }

            xmlFileVersion = string.Empty;
            return false;
        }

        private void ValidateMZXmlFileVersion(string xmlWithFileVersion)
        {
            try
            {
                mFileVersion = string.Empty;
                string message;

                if (!ExtractMzXmlFileVersion(xmlWithFileVersion, out mFileVersion))
                {
                    message = "Unknown mzXML file version; expected text not found in xmlWithFileVersion";

                    if (mParseFilesWithUnknownVersion)
                    {
                        message += "; attempting to parse since ParseFilesWithUnknownVersion = True";
                    }
                    else
                    {
                        mAbortProcessing = true;
                        message += "; aborting read";
                    }

                    OnErrorEvent(message);
                    return;
                }

                if (string.IsNullOrWhiteSpace(mFileVersion))
                    return;

                if (mFileVersion.IndexOf("mzxml_2", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    mFileVersion.IndexOf("mzxml_3", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return;
                }

                // fileVersion contains mzXML_ but not mxXML_2 or mxXML_3
                // Thus, assume unknown version
                // Log error and abort if mParseFilesWithUnknownVersion = False
                message = "Unknown mzXML file version: " + mFileVersion;

                if (mParseFilesWithUnknownVersion)
                {
                    message += "; attempting to parse since ParseFilesWithUnknownVersion = True";
                }
                else
                {
                    mAbortProcessing = true;
                    message += "; aborting read";
                }

                OnErrorEvent(message);
            }
            catch (Exception ex)
            {
                OnErrorEvent("Error in ValidateMZXmlFileVersion", ex);
                mFileVersion = string.Empty;
            }
        }
    }
}