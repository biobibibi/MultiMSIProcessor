// -------------------------------------------------------------------------------
// Written by Matthew Monroe for the Department of Energy (PNNL, Richland, WA)
// Copyright 2021, Battelle Memorial Institute.  All Rights Reserved.
//
// E-mail: matthew.monroe@pnl.gov or proteomics@pnnl.gov
// Website: https://github.com/PNNL-Comp-Mass-Spec/ or https://panomics.pnnl.gov/ or https://www.pnnl.gov/integrative-omics
// -------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml;
using PRISM;
using static System.Windows.Forms.DataFormats;

// ReSharper disable UnusedMember.Global

namespace MSDataFileReader
{
    /// <summary>
    /// This is the base class for the mzXML and mzData readers
    /// </summary>
    public abstract class MsXMLFileReaderBaseClass : MsDataFileReaderBaseClass
    {
        /// <summary>
        /// Constructor
        /// </summary>
        protected MsXMLFileReaderBaseClass()
        {
            // ReSharper disable once VirtualMemberCallInConstructor
            InitializeLocalVariables();
        }

        ~MsXMLFileReaderBaseClass()
        {
            try
            {
                mDataFileOrTextStream?.Close();
            }
            catch (Exception)
            {
                // Ignore errors here
            }

            try
            {
                mXMLReader?.Close();
            }
            catch (Exception)
            {
                // Ignore errors here
            }
        }

        private struct ElementInfoType
        {
            public string Name;

            public int Depth;
        }

        protected TextReader mDataFileOrTextStream;

        protected XmlReader mXMLReader;

        protected bool mSpectrumFound;

        /// <summary>
        /// When true, the base-64 encoded data in the file is not parsed,
        /// thus speeding up the reader
        /// </summary>
        protected bool mSkipBinaryData;

        protected bool mSkipNextReaderAdvance;

        protected bool mSkippedStartElementAdvance;

        // Last element name handed off from reader; set to "" when an End Element is encountered
        protected string mCurrentElement;

        protected Stack mParentElementStack;

        public int SAXParserLineNumber
        {
            get
            {
                if (mXMLReader is XmlTextReader xmlReader)
                {
                    return xmlReader.LineNumber;
                }

                return 0;
            }
        }

        public int SAXParserColumnNumber
        {
            get
            {
                if (mXMLReader is XmlTextReader xmlReader)
                {
                    return xmlReader.LinePosition;
                }

                return 0;
            }
        }

        public bool SkipBinaryData
        {
            get => mSkipBinaryData;

            set => mSkipBinaryData = value;
        }

        public override void CloseFile()
        {
            mXMLReader?.Close();

            mDataFileOrTextStream = null;
            mInputFilePath = string.Empty;
        }

        /// <summary>
        /// Convert a timespan to an XML duration
        /// </summary>
        /// <remarks>
        /// <para>
        /// XML duration value is typically of the form "PT249.559S" or "PT4M9.559S"
        /// where the S indicates seconds and M indicates minutes
        /// Thus, "PT249.559S" means 249.559 seconds while
        /// "PT4M9.559S" means 4 minutes plus 9.559 seconds
        /// </para>
        /// <para>
        /// When trimLeadingZeroValues is true, for TimeSpan 3 minutes, returns P3M0S rather than P0Y0M0DT0H3M0S
        /// </para>
        /// </remarks>
        /// <param name="timeSpan"></param>
        /// <param name="trimLeadingZeroValues">When false, return the full specification; otherwise, remove any leading zero-value entries</param>
        /// <param name="secondsValueDigitsAfterDecimal"></param>
        /// <returns>XML duration string</returns>
        public static string ConvertTimeFromTimespanToXmlDuration(TimeSpan timeSpan, bool trimLeadingZeroValues, byte secondsValueDigitsAfterDecimal = 3)
        {
            const string ZERO_SOAP_DURATION_FULL = "P0Y0M0DT0H0M0S";
            const string ZERO_SOAP_DURATION_SHORT = "PT0S";

            var success = ConvertTimeFromTimespanToXmlDuration(
                timeSpan, trimLeadingZeroValues, secondsValueDigitsAfterDecimal, out var xmlDuration, out var isZero);

            if (success && !isZero)
                return xmlDuration;

            return trimLeadingZeroValues ? ZERO_SOAP_DURATION_SHORT : ZERO_SOAP_DURATION_FULL;
        }

        private static bool ConvertTimeFromTimespanToXmlDuration(
            TimeSpan timeSpan,
            bool trimLeadingZeroValues,
            byte secondsValueDigitsAfterDecimal,
            out string xmlDuration,
            out bool isZero)
        {
            isZero = false;

            try
            {
                if (timeSpan.Equals(TimeSpan.Zero))
                {
                    isZero = true;
                    xmlDuration = string.Empty;
                    return true;
                }

                //xmlDuration = System.Runtime.Remoting.Metadata.W3cXsd2001.SoapDuration.ToString(timeSpan);
                xmlDuration = timeSpan.ToString();

                if (xmlDuration.Length == 0)
                {
                    isZero = true;
                    return true;
                }

                if (xmlDuration[0] == '-')
                {
                    xmlDuration = xmlDuration.Substring(1);
                }

                if (secondsValueDigitsAfterDecimal < 9)
                {
                    // Look for "M\.\d+S"
                    var reSecondsRegEx = new Regex(@"M(\d+\.\d+)S");
                    var match = reSecondsRegEx.Match(xmlDuration);

                    if (match.Success && double.TryParse(match.Groups[1].Captures[0].Value, out var seconds))
                    {
                        xmlDuration = string.Format("{0}{1}S",
                            xmlDuration.Substring(0, match.Groups[1].Index),
                            Math.Round(seconds, secondsValueDigitsAfterDecimal));
                    }
                }

                if (!trimLeadingZeroValues)
                    return true;

                var dateIndex = xmlDuration.IndexOf('P');
                var timeIndex = xmlDuration.IndexOf('T');
                var charIndex = xmlDuration.IndexOf("P0Y", StringComparison.Ordinal);

                if (charIndex >= 0 && charIndex < timeIndex)
                {
                    charIndex++;
                    var charIndex2 = xmlDuration.IndexOf("Y0M", charIndex, StringComparison.Ordinal);

                    if (charIndex2 > 0 && charIndex < timeIndex)
                    {
                        charIndex = charIndex2 + 1;
                        charIndex2 = xmlDuration.IndexOf("M0D", charIndex, StringComparison.Ordinal);

                        if (charIndex2 > 0 && charIndex < timeIndex)
                        {
                            charIndex = charIndex2 + 1;
                        }
                    }
                }

                if (charIndex <= 0)
                    return true;

                xmlDuration = xmlDuration.Substring(0, dateIndex + 1) + xmlDuration.Substring(charIndex + 2);
                timeIndex = xmlDuration.IndexOf('T');
                charIndex = xmlDuration.IndexOf("T0H", timeIndex, StringComparison.Ordinal);

                if (charIndex > 0)
                {
                    charIndex++;
                    var charIndex2 = xmlDuration.IndexOf("H0M", charIndex, StringComparison.Ordinal);

                    if (charIndex2 > 0)
                    {
                        charIndex = charIndex2 + 1;
                    }
                }

                if (charIndex > 0)
                {
                    xmlDuration = xmlDuration.Substring(0, timeIndex + 1) + xmlDuration.Substring(charIndex + 2);
                }

                return true;
            }
            catch (Exception ex)
            {
                ConsoleMsgUtils.ShowWarning("Error in ConvertTimeFromTimespanToXmlDuration: {0}", ex.Message);
                xmlDuration = string.Empty;
                return false;
            }
        }

        /// <summary>
        /// Convert an XML duration to a TimeSpan
        /// </summary>
        /// <remarks>
        /// XML duration value is typically of the form "PT249.559S" or "PT4M9.559S"
        /// where the S indicates seconds and M indicates minutes
        /// Thus, "PT249.559S" means 249.559 seconds while
        /// "PT4M9.559S" means 4 minutes plus 9.559 seconds
        /// </remarks>
        /// <param name="time"></param>
        /// <param name="defaultTimeSpan"></param>
        /// <returns>TimeSpan, or defaultTimeSpan if an error</returns>
        public static TimeSpan ConvertTimeFromXmlDurationToTimespan(string time, TimeSpan defaultTimeSpan)
        {
            // Official definition:
            // A length of time given in the ISO 8601 extended format: PnYnMnDTnHnMnS. The number of seconds
            // can be a decimal or an integer. All the other values must be non-negative integers. For example,
            // P1Y2M3DT4H5M6.7S is one year, two months, three days, four hours, five minutes, and 6.7 seconds.

            TimeSpan timeSpan;

            try
            {
                //timeSpan = System.Runtime.Remoting.Metadata.W3cXsd2001.SoapDuration.Parse(time);
                timeSpan = Parse1(time);
            }
            catch (Exception)
            {
                timeSpan = defaultTimeSpan;
            }

            return timeSpan;
        }

        protected float GetAttribTimeValueMinutes(string attributeName)
        {
            try
            {
                var timeSpan = ConvertTimeFromXmlDurationToTimespan(GetAttribValue(attributeName, "PT0S"), new TimeSpan(0L));
                return (float)timeSpan.TotalMilliseconds / (1000*60);
            }
            catch (Exception)
            {
                return 0f;
            }
        }

        protected string GetAttribValue(string attributeName, string defaultValue)
        {
            try
            {
                if (mXMLReader.HasAttributes)
                {
                    return mXMLReader.GetAttribute(attributeName) ?? defaultValue;
                }

                return defaultValue;
            }
            catch (Exception)
            {
                return defaultValue;
            }
        }

        protected int GetAttribValue(string attributeName, int defaultValue)
        {
            try
            {
                return int.Parse(GetAttribValue(attributeName, defaultValue.ToString()));
            }
            catch (Exception)
            {
                return defaultValue;
            }
        }

        protected float GetAttribValue(string attributeName, float defaultValue)
        {
            try
            {
                return float.Parse(GetAttribValue(attributeName, defaultValue.ToString(CultureInfo.InvariantCulture)));
            }
            catch (Exception)
            {
                return defaultValue;
            }
        }

        protected bool GetAttribValue(string attributeName, bool defaultValue)
        {
            try
            {
                return CBoolSafe(GetAttribValue(attributeName, defaultValue.ToString()), defaultValue);
            }
            catch (Exception)
            {
                return defaultValue;
            }
        }

        protected double GetAttribValue(string attributeName, double defaultValue)
        {
            try
            {
                return double.Parse(GetAttribValue(attributeName, defaultValue.ToString(CultureInfo.InvariantCulture)));
            }
            catch (Exception)
            {
                return defaultValue;
            }
        }

        protected abstract SpectrumInfo GetCurrentSpectrum();

        /// <summary>
        /// Obtain the element name one level up from depth
        /// </summary>
        /// <remarks>
        /// If depth = 0, returns the element name one level up from the last entry in mParentElementStack
        /// </remarks>
        /// <param name="elementDepth"></param>
        protected string GetParentElement(int elementDepth = 0)
        {
            if (elementDepth == 0)
            {
                elementDepth = mParentElementStack.Count;
            }

            if (elementDepth < 2 || elementDepth > mParentElementStack.Count)
                return string.Empty;

            try
            {
                var elementInfo = (ElementInfoType)mParentElementStack.ToArray()[mParentElementStack.Count - elementDepth + 1];
                return elementInfo.Name;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        protected override string GetInputFileLocation()
        {
            return "Line " + SAXParserLineNumber + ", Column " + SAXParserColumnNumber;
        }

        protected abstract void InitializeCurrentSpectrum();

        protected override void InitializeLocalVariables()
        {
            // This method is called from OpenFile and OpenTextStream;
            // thus, do not update mSkipBinaryData

            base.InitializeLocalVariables();
            mSkipNextReaderAdvance = false;
            mSkippedStartElementAdvance = false;
            mSpectrumFound = false;
            mCurrentElement = string.Empty;

            if (mParentElementStack is null)
            {
                mParentElementStack = new Stack();
            }
            else
            {
                mParentElementStack.Clear();
            }
        }

        /// <summary>
        /// Open the data file
        /// </summary>
        /// <param name="inputFilePath"></param>
        /// <returns>True if successful, false if an error</returns>
        public override bool OpenFile(string inputFilePath)
        {
            try
            {
                var success = OpenFileInit(inputFilePath);

                if (!success)
                    return false;

                // Initialize the stream reader and the XML Text Reader (set to skip all whitespace)
                mDataFileOrTextStream = new StreamReader(inputFilePath);
                mXMLReader = new XmlTextReader(mDataFileOrTextStream) { WhitespaceHandling = WhitespaceHandling.None };
                mErrorMessage = string.Empty;
                InitializeLocalVariables();
                ResetProgress("Parsing " + Path.GetFileName(inputFilePath));
                return true;
            }
            catch (Exception ex)
            {
                mErrorMessage = "Error opening file: " + inputFilePath + "; " + ex.Message;
                OnErrorEvent(mErrorMessage, ex);
                return false;
            }
        }

        /// <summary>
        /// Open the text stream
        /// </summary>
        /// <param name="textStream"></param>
        /// <returns>True if successful, false if an error</returns>
        public override bool OpenTextStream(string textStream)
        {
            // Make sure any open file or text stream is closed
            CloseFile();

            try
            {
                mInputFilePath = "TextStream";

                // Initialize the stream reader and the XML Text Reader (set to skip all whitespace)
                mDataFileOrTextStream = new StringReader(textStream);
                var reader = new XmlTextReader(mDataFileOrTextStream) { WhitespaceHandling = WhitespaceHandling.None };
                mXMLReader = reader;
                mErrorMessage = string.Empty;
                InitializeLocalVariables();
                ResetProgress("Parsing text stream");
                return true;
            }
            catch (Exception ex)
            {
                mErrorMessage = "Error opening text stream";
                OnErrorEvent(mErrorMessage, ex);
                return false;
            }
        }

        protected string ParentElementStackRemove()
        {
            // Removes the most recent entry from mParentElementStack and returns it
            if (mParentElementStack.Count == 0)
            {
                return string.Empty;
            }

            var elementInfo = (ElementInfoType)mParentElementStack.Pop();
            return elementInfo.Name;
        }

        /// <summary>
        /// Adds a new entry to the end of mParentElementStack
        /// </summary>
        /// <param name="xmlReader"></param>
        protected void ParentElementStackAdd(XmlReader xmlReader)
        {
            // Since the XML Text Reader doesn't recognize implicit end elements (e.g. the "/>" characters at
            // the end of <City name="Laramie" />) we need to compare the depth of the current element with
            // the depth of the element at the top of the stack
            // If the depth values are the same, we pop the top element off and push the new element on
            // If the depth values are not the same, we push the new element on

            ElementInfoType elementInfo;

            if (mParentElementStack.Count > 0)
            {
                elementInfo = (ElementInfoType)mParentElementStack.Peek();

                if (elementInfo.Depth == xmlReader.Depth)
                {
                    mParentElementStack.Pop();
                }
            }

            elementInfo.Name = xmlReader.Name;
            elementInfo.Depth = xmlReader.Depth;
            mParentElementStack.Push(elementInfo);
        }

        protected abstract void ParseStartElement();

        protected abstract void ParseElementContent();

        protected abstract void ParseEndElement();

        /// <summary>
        /// Read the next spectrum from an mzXML or mzData file
        /// </summary>
        /// <param name="spectrumInfo"></param>
        /// <returns>True if a spectrum is found, otherwise, returns False</returns>
        public override bool ReadNextSpectrum(out SpectrumInfo spectrumInfo)
        {
            try
            {
                InitializeCurrentSpectrum();
                mSpectrumFound = false;

                if (mXMLReader is null)
                {
                    spectrumInfo = new SpectrumInfo();
                    mErrorMessage = "Data file not currently open";
                    return false;
                }

                if (mDataFileOrTextStream != null)
                {
                    if (mDataFileOrTextStream is StreamReader streamReader)
                    {
                        UpdateProgress(streamReader.BaseStream.Position / (double)streamReader.BaseStream.Length * 100.0d);
                    }
                    else if (mXMLReader is XmlTextReader xmlReader)
                    {
                        // Note that 1000 is an arbitrary value for the number of lines in the input stream
                        // (only needed if mDataFileOrTextStream is a StringReader)
                        UpdateProgress(xmlReader.LineNumber % 1000 / 1000d * 100.0d);
                    }
                }

                var validData = true;

                while (!mSpectrumFound && validData && !mAbortProcessing && mXMLReader.ReadState is ReadState.Initial or ReadState.Interactive)
                {
                    mSpectrumFound = false;

                    if (mSkipNextReaderAdvance)
                    {
                        mSkipNextReaderAdvance = false;

                        try
                        {
                            if (mXMLReader.NodeType == XmlNodeType.Element)
                            {
                                mSkippedStartElementAdvance = true;
                            }
                        }
                        catch (Exception ex)
                        {
                            // Ignore Errors Here
                        }
                    }
                    else
                    {
                        mSkippedStartElementAdvance = false;
                        validData = mXMLReader.Read();
                        XMLTextReaderSkipWhitespace();
                    }

                    if (!validData || mXMLReader.ReadState != ReadState.Interactive)
                        continue;

                    switch (mXMLReader.NodeType)
                    {
                        case XmlNodeType.Element:
                            ParseStartElement();
                            break;

                        case XmlNodeType.EndElement:
                            ParseEndElement();
                            break;

                        case XmlNodeType.Text:
                            ParseElementContent();
                            break;
                    }
                }

                spectrumInfo = GetCurrentSpectrum();

                if (mSpectrumFound)
                {
                    mScanCountRead++;

                    if (!ReadingAndStoringSpectra)
                    {
                        if (mInputFileStats.ScanCount < mScanCountRead)
                            mInputFileStats.ScanCount = mScanCountRead;

                        UpdateFileStats(mInputFileStats.ScanCount, spectrumInfo.ScanNumber, false);
                    }
                }
            }
            catch (Exception ex)
            {
                OnErrorEvent("Error in ReadNextSpectrum", ex);
                spectrumInfo = new SpectrumInfo();
            }

            return mSpectrumFound;
        }

        protected string XMLTextReaderGetInnerText()
        {
            bool success;

            // ReSharper disable once ConvertIfStatementToConditionalTernaryExpression
            if (mXMLReader.NodeType == XmlNodeType.Element)
            {
                // Advance the reader so that we can read the value
                success = mXMLReader.Read();
            }
            else
            {
                success = true;
            }

            if (success && mXMLReader.NodeType != XmlNodeType.Whitespace && mXMLReader.HasValue)
            {
                return mXMLReader.Value;
            }

            return string.Empty;
        }

        private void XMLTextReaderSkipWhitespace()
        {
            try
            {
                if (mXMLReader.NodeType == XmlNodeType.Whitespace)
                {
                    // Whitespace; read the next node
                    mXMLReader.Read();
                }
            }
            catch (Exception)
            {
                // Ignore Errors Here
            }
        }

        /// <summary>
        /// A repalcement of System.Runtime.Remoting.Metadata.W3cXsd2001.SoapDuration.ToString() function
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="FormatException"></exception>
        public static TimeSpan Parse1(string input)
        {
            if (string.IsNullOrEmpty(input))
                throw new ArgumentNullException(nameof(input));

            bool isNegative = input[0] == '-';
            int startIndex = isNegative ? 1 : 0;

            if (input[startIndex] != 'P')
                throw new FormatException("Invalid format.");

            int year = 0, month = 0, day = 0, hour = 0, minute = 0;
            double second = 0;

            int lastIndex = input.Length - 1;
            int currentIndex = startIndex + 1;

            while (lastIndex > 0)
            {
                int number = 0;


                while (currentIndex <= lastIndex && char.IsDigit(input[currentIndex]))
                {
                    number = number * 10 + (int)char.GetNumericValue(input[currentIndex]);
                    currentIndex++;
                }

                if (currentIndex > lastIndex)
                    break;

                char unit = input[currentIndex];

                switch (unit)
                {
                    case 'T':
                        goto case 'H';
                    case 'Y':
                        year = number;
                        break;
                    case 'M':
                        if (currentIndex < lastIndex && currentIndex < input.IndexOf('T'))
                            month = number;
                        else
                            minute = number;
                        break;
                    case 'D':
                        day = number;
                        break;
                    case 'H':
                        hour = number;
                        break;
                    case 'S':
                        if (currentIndex <= lastIndex && input.Contains('.'))
                        {
                            int xx = currentIndex - input.IndexOf('.') -1;
                            second += (double)(number / Math.Pow(10, xx));
                        }
                        else
                            second = number;
                        break;
                    case '.':
                        second = number;
                        break;
                    default:
                        throw new FormatException("Invalid format.");
                }

                currentIndex++;
            }

            if (isNegative)
                return new TimeSpan(-(year * 365 + month * 30 + day), hour, minute, (int)second, (int)((second % 1) * 1000));
            else
                return new TimeSpan(year * 365 + month * 30 + day, hour, minute, (int)second, (int)((second % 1) * 1000));
        }
    }
}