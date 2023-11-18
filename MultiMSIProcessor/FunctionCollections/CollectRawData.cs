// Copyright 2023 Siwei Bi, Manjiangcuo Wang, and Dan Du
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// you may obtain a copy of the License at
// 
//      http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using RDotNet;
using Serilog;
using ThermoFisher.CommonCore.Data.Business;
using ThermoFisher.CommonCore.Data.Interfaces;

namespace MultiMSIProcessor.FunctionCollections
{
    internal class CollectRawData
    {
        /// <summary>
        /// a thread creator and a error checking point
        /// </summary>
        /// <param name="rawFileThreadManager"></param>
        /// <returns></returns>
        public static ConcurrentDictionary<string, List<double>> ReadInFromRaw(IRawFileThreadManager rawFileThreadManager, int[] scansReference, double intervalTime)
        {
            var staticRawFile = rawFileThreadManager.CreateThreadAccessor();
            var err = staticRawFile.FileError;
            if (err.HasError)
            {
                Console.WriteLine("ERROR: {0} reports error code: {1}. The associated message is: {2}",
                    Path.GetFileName(staticRawFile.FileName), err.ErrorCode, err.ErrorMessage);
                Console.WriteLine("Skipping this file");

                Log.Error("{FILE} reports error code: {ERRORCODE}. The associated message is: {ERRORMESSAGE}",
                Path.GetFileName(staticRawFile.FileName), err.ErrorCode, err.ErrorMessage);
                return null;
            }
            else
            {
                //(ConcurrentDictionary<double, List<double>> mz_data, ConcurrentDictionary< double, int > mzMissingValue) = AFACentroidCollectionToList.CentroidCollectionData2(staticRawFile);
                ConcurrentDictionary<string, List<double>> mz_data = AFACentroidCollectionToList.CentroidCollectionData(staticRawFile, scansReference, intervalTime);
                //return (mz_data, mzMissingValue);
                return mz_data;
            }
        }

        public static ConcurrentDictionary<double, ConcurrentDictionary<int, double[]>> ReadInFromRaw_2(IRawFileThreadManager rawFileThreadManager, 
            int[] scansReference, double intervalTime, int rawFileNum, double lower, double upper)
        {
            var staticRawFile = rawFileThreadManager.CreateThreadAccessor();
            var err = staticRawFile.FileError;
            if (err.HasError)
            {
                Console.WriteLine("ERROR: {0} reports error code: {1}. The associated message is: {2}",
                    Path.GetFileName(staticRawFile.FileName), err.ErrorCode, err.ErrorMessage);
                Console.WriteLine("Skipping this file");

                Log.Error("{FILE} reports error code: {ERRORCODE}. The associated message is: {ERRORMESSAGE}",
                Path.GetFileName(staticRawFile.FileName), err.ErrorCode, err.ErrorMessage);
                return null;
            }
            else
            {
                //(ConcurrentDictionary<double, List<double>> mz_data, ConcurrentDictionary< double, int > mzMissingValue) = AFACentroidCollectionToList.CentroidCollectionData2(staticRawFile);
                ConcurrentDictionary<double, ConcurrentDictionary<int, double[]>> mz_data = AFACentroidCollectionToList.CentroidCollectionData_v2(staticRawFile, scansReference, intervalTime, rawFileNum, lower, upper);
                //return (mz_data, mzMissingValue);
                return mz_data;
            }
        }

        /// <summary>
        /// 1st version MRM data
        /// </summary>
        /// <param name="rawFileThreadManager"></param>
        /// <returns></returns>
        public static ConcurrentDictionary<string, List<double>> ReadInFromRawMRM(IRawFileThreadManager rawFileThreadManager)
        {
            var staticRawFile = rawFileThreadManager.CreateThreadAccessor();
            var err = staticRawFile.FileError;
            if (err.HasError)
            {
                Console.WriteLine("ERROR: {0} reports error code: {1}. The associated message is: {2}",
                    Path.GetFileName(staticRawFile.FileName), err.ErrorCode, err.ErrorMessage);
                Console.WriteLine("Skipping this file");

                Log.Error("{FILE} reports error code: {ERRORCODE}. The associated message is: {ERRORMESSAGE}",
                Path.GetFileName(staticRawFile.FileName), err.ErrorCode, err.ErrorMessage);
                return null;
            }
            else
            {
                staticRawFile.SelectInstrument(Device.MS, 1); //instrumentType and instrumentIndex (Stream number): based 1; must be called before reading
                ConcurrentDictionary<string, List<double>> mz_data = AFACentroidCollectionToList.CentroidCollectionDataMRM_v1(staticRawFile);
                return mz_data;
            }
        }

        /// <summary>
        /// To collect the precursor and ionized ion information in DDMS2 data
        /// </summary>
        /// <param name="rawFileThreadManager"></param>
        /// <returns></returns>
        public static ConcurrentDictionary<int, List<double[]>> ReadInFromRawMRM2(IRawFileThreadManager rawFileThreadManager)
        {
            var staticRawFile = rawFileThreadManager.CreateThreadAccessor();
            var err = staticRawFile.FileError;
            if (err.HasError)
            {
                Console.WriteLine("ERROR: {0} reports error code: {1}. The associated message is: {2}",
                    Path.GetFileName(staticRawFile.FileName), err.ErrorCode, err.ErrorMessage);
                Console.WriteLine("Skipping this file");

                Log.Error("{FILE} reports error code: {ERRORCODE}. The associated message is: {ERRORMESSAGE}",
                Path.GetFileName(staticRawFile.FileName), err.ErrorCode, err.ErrorMessage);
                return null;
            }
            else
            {
                staticRawFile.SelectInstrument(Device.MS, 1); //instrumentType and instrumentIndex (Stream number): based 1; must be called before reading
                ConcurrentDictionary<int, List<double[]>> mz_data = AFACentroidCollectionToList.CentroidCollectionDataMRM_v2(staticRawFile);
                return mz_data;
            }
        }

        /// <summary>
        /// To collect the precursor and ionized ion intensity info in AIF data
        /// </summary>
        /// <param name="rawFileThreadManager"></param>
        /// <returns></returns>
        public static (ConcurrentDictionary<string, List<double>>, ConcurrentDictionary<string, List<double>>) ReadInFromRawMRM3(IRawFileThreadManager rawFileThreadManager)
        {
            var staticRawFile = rawFileThreadManager.CreateThreadAccessor();
            var err = staticRawFile.FileError;
            if (err.HasError)
            {
                Console.WriteLine("ERROR: {0} reports error code: {1}. The associated message is: {2}",
                    Path.GetFileName(staticRawFile.FileName), err.ErrorCode, err.ErrorMessage);
                Console.WriteLine("Skipping this file");

                Log.Error("{FILE} reports error code: {ERRORCODE}. The associated message is: {ERRORMESSAGE}",
                Path.GetFileName(staticRawFile.FileName), err.ErrorCode, err.ErrorMessage);
                return (null, null);
            }
            else
            {
                staticRawFile.SelectInstrument(Device.MS, 1); //instrumentType and instrumentIndex (Stream number): based 1; must be called before reading
                (ConcurrentDictionary<string, List<double>> mz_data_precursor,
                    ConcurrentDictionary<string, List<double>> mz_data_ionized) =
                    AFACentroidCollectionToList.CentroidCollectionDataMRM_v3(staticRawFile);
                return (mz_data_precursor, mz_data_ionized);
            }
        }


        /// <summary>
        /// To collect the mz and ion intensity info in imzML data
        /// </summary>
        /// <param name="rawFileThreadManager"></param>
        /// <param name="scansReference"></param>
        /// <param name="intervalTime"></param>
        /// <returns></returns>
        public static (int, int) ReadInFromimzML(string imzMLFile, REngine engine,
            ConcurrentDictionary<double, ConcurrentDictionary<int, double[]>> mz_data, double lower, double upper)
        {
            engine.SetSymbol("filename", engine.CreateCharacter(imzMLFile));

            engine.Evaluate("library(\"rmsi\")\r\nimagespectra <- importImzMl(filename, centroided =T)\r\nimagespectra = imagespectra[-length(imagespectra)]\r\nelementsimzML<-length(imagespectra)\r\nimagepos <- sapply(imagespectra, function(x)metaData(x)$imaging$pos)\r\nminx<-min(imagepos[1,])\r\nmaxx<-max(imagepos[1,])\r\nminy<-min(imagepos[2,])\r\nmaxy<-max(imagepos[2,])\r\nxrange<-maxx-minx+1\r\nyrange<-maxy-miny+1\r\n");

            int columnNumber = engine.Evaluate("xrange").AsInteger().First();
            int rowNumber = engine.Evaluate("yrange").AsInteger().First();
            int spectrumNumber = engine.Evaluate("elementsimzML").AsInteger().First();

            int minCol = engine.Evaluate("minx").AsInteger().First();
            int minRow = engine.Evaluate("miny").AsInteger().First();
            // in the value, double is the intensity, int[] first is the row, int[] second is the column

            List<double> mzs = new();
            List<double> intensity = new();

            for (int i = 0; i < spectrumNumber; i++)
            //Parallel.For(0, spectrumNumber - 1, i =>
            {
                engine.SetSymbol("m", engine.CreateInteger(i + 1));
                mzs = engine.Evaluate("mass(imagespectra[[m]])")
                        .AsNumeric()
                        .Select(x => Math.Round(AFACentroidCollectionToList.ProcessTheMz(x), 4))
                        .ToList();
                intensity = engine.Evaluate("intensity(imagespectra[[m]])")
                        .AsNumeric()
                        .ToList();

                if (lower != 0 & upper != 1000000.0)
                {
                    intensity = intensity
                        .Where((v, Index) => mzs[Index] < upper & mzs[Index] > lower)
                        .ToList();
                    mzs = mzs.Where(x => x < upper & x > lower).ToList();
                }
                
                int col = engine.Evaluate("metaData(imagespectra[[m]])$imaging$pos[[1]]").AsInteger().First() - minCol;
                int row = engine.Evaluate("metaData(imagespectra[[m]])$imaging$pos[[2]]").AsInteger().First() - minRow;

                AFAMassIntensityPipeline.GatheringTheSpectrum(col, row, intensity, mzs, mz_data, columnNumber);
                mzs.Clear();
                intensity.Clear();
            }
            return (columnNumber, rowNumber);
        }

        public static ConcurrentDictionary<string, int> CalculateMissingInimzML(ConcurrentDictionary<string, double[][]> mzhHeatbin)
        {
            ConcurrentDictionary<string, int> missingValDict = new();

            foreach (KeyValuePair<string, double[][]> val in mzhHeatbin)
            {
                int missingVal = 0;
                for (int i = 0; i < val.Value.Length; i++)
                {
                    missingVal += val.Value[i].Where(x => x == 0).Count();
                }
                missingValDict.TryAdd(val.Key, missingVal);
            }
            return missingValDict;
        }


        public static ConcurrentDictionary<string, double[][]> ReadInFromMALDI_Thermo(string file, int col, int rows)
        {
            IRawFileThreadManager rawFile = RawFileReaderFactory.CreateThreadManager(file);
            var rawFile2 = rawFile.CreateThreadAccessor();
            rawFile2.SelectInstrument(Device.MS, 1);
            ConcurrentDictionary<string, double[][]> mz_data = new();

            CentroidStream centroidAll = rawFile2.GetCentroidStream(1, false);

            int[] scans = rawFile2.GetFilteredScanEnumerator(rawFile2.GetFilterFromString("")).ToArray(); // get all scans

            for (int i = 0; i < col; i++) // scan = 1 2 3 ...
            {
                double[][] intensity_list_all = new double[rows][];
                for (int j = 0; j < rows; j++)
                {
                    // give the initial value 0 to each scan of mz
                    double[] intensity_list = Enumerable.Repeat((double)0, col).ToArray();
                    intensity_list_all[j] = intensity_list;
                }
                for (int j = 0; j < rows; j++)
                {
                    int index;
                    if (i == 0) { index = j; } else { index = i * j; }

                    string mZ = Math.Round(AFACentroidCollectionToList.ProcessTheMz(centroidAll.Masses[index]), 4).ToString(); //mz could be duplicated after this step
                    double intensity = centroidAll.Intensities[index];

                    if (!mz_data.ContainsKey(mZ))
                    {
                        mz_data.TryAdd(mZ, intensity_list_all);
                        mz_data[mZ][i][j] = intensity;
                    }
                    else //if the mz does exist
                    {
                        //if the mz intensity is 0, which means the initial value, then give the intensity
                        if (mz_data[mZ][i][j] == 0)
                        {
                            mz_data[mZ][i][j] = intensity;
                        }
                        //if the mz duplicates, then the intensity would take average
                        else
                        {
                            mz_data[mZ][i][j] = (mz_data[mZ][i][j] + intensity) / 2;
                        }
                    }
                }
            }
            return mz_data;
        }
    }
}
