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

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Policy;
using System.Web;
using System.Windows.Forms.VisualStyles;
using System.Xml.Linq;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.ApplicationServices;
using MSDataFileReader;
using RDotNet;
using Serilog.Core;
using ThermoFisher.CommonCore.Data.Business;
using ThermoFisher.CommonCore.Data.Interfaces;
using static Microsoft.WindowsAPICodePack.Shell.PropertySystem.SystemProperties.System;

namespace MultiMSIProcessor.FunctionCollections
{
    static internal class AFAMassIntensityPipeline
    {
        /// <summary>
        /// The filter methods
        /// </summary>
        /// <param name="mZandHeatbinDict"></param>
        /// <param name="mZandMissingDict"></param>
        /// <param name="rawFileNum"></param>
        /// <param name="colMax"></param>
        /// <returns></returns>
        public static ConcurrentDictionary<string, double[][]> FilterTheMissing(
            ConcurrentDictionary<string, double[][]> mZandHeatbinDict,
            ConcurrentDictionary<string, int> mZandMissingDict,
            int rawFileNum,
            int colMax)
        {
            int total = rawFileNum * colMax;
            Parallel.ForEach(mZandHeatbinDict, eachmZandHeatbin =>
            {
                if (mZandMissingDict[eachmZandHeatbin.Key] > total * 0.8)
                {
                    mZandHeatbinDict.Remove(eachmZandHeatbin.Key, out _);
                }
            });
            return mZandHeatbinDict;
        }


        public static ConcurrentDictionary<double, ConcurrentDictionary<int, double[]>> FilterTheMissing_v2(
            ConcurrentDictionary<double, ConcurrentDictionary<int, double[]>> mZandHeatbinDict,
            ConcurrentDictionary<double, int> mZandMissingDict,
            int rawFileNum,
            int colMax)
        {
            int total = rawFileNum * colMax;
            Parallel.ForEach(mZandHeatbinDict, eachmZandHeatbin =>
            {
                if (mZandMissingDict[eachmZandHeatbin.Key] > total * 0.8)
                {
                    mZandHeatbinDict.Remove(eachmZandHeatbin.Key, out _);
                }
            });
            return mZandHeatbinDict;
        }


        /// <summary>
        /// reconstruct the data from each row to a whole picture
        /// in the heatBin[][], the first one is the row number and the latter is the column number.
        /// </summary>
        /// <param name="rawFileNumberTotal"></param>
        /// <param name="colMax"></param>
        /// <param name="MassDataInEachRawCollections"></param>
        /// <returns></returns>
        public static (ConcurrentDictionary<string, double[][]>, ConcurrentDictionary<string, int>) ProcessTheMzInAllTheRaw(
            int rawFileNumberTotal, int colMax,
            ConcurrentDictionary<int, ConcurrentDictionary<string, List<double>>> MassDataInEachRawCollections)
        {
            ConcurrentDictionary<string, double[][]> heatBinDict = new();
            ConcurrentDictionary<string, int> missingValueEachmzDict = new();

            var mzSelection = MassDataInEachRawCollections
                .SelectMany(x => x.Value.Keys, (eachRaw, mzList, index) => new { eachRaw, mzList, index })
                .Select(x => new
                {
                    rawFileIndex = x.eachRaw.Key,
                    rawFileMzList = x.mzList,
                    indexWithinTheDict = x.index
                })
                .GroupBy(y => y.rawFileMzList)
                .Select(x => new
                {
                    distinctMZ = x.Key,
                    rawFileNumber = x.Select(z => z.rawFileIndex).ToArray(),
                    indexArrary = x.Select(z => z.indexWithinTheDict).ToArray()
                })
                .Where(x => x.indexArrary.Length > rawFileNumberTotal * 0.2)
                .ToList();

            Parallel.For(0, mzSelection.Count, i =>
            {
                int missingValueForEachmz = 0;
                double[][] heatBins = new double[rawFileNumberTotal][];

                for (int rawIndex = 0; rawIndex < rawFileNumberTotal; rawIndex++)
                {
                    heatBins[rawIndex] = Enumerable.Repeat((double)0, colMax).ToArray();
                }

                string keyToSave = mzSelection[i].distinctMZ;

                if (mzSelection[i].rawFileNumber.Length > 1) // when >1 mzs are selected by .0 round
                {
                    int[] rawGroup = mzSelection[i].rawFileNumber;
                    int[] indexArrary = mzSelection[i].indexArrary;
                    for (int m = 0; m < mzSelection[i].rawFileNumber.Length; m++)
                    {

                        int rawData = rawGroup[m];
                        int index = indexArrary[m];

                        heatBins[rawData] = MassDataInEachRawCollections[rawData].ElementAt(index).Value.ToArray();

                    }

                    for (int rawIndex = 0; rawIndex < rawFileNumberTotal; rawIndex++)
                    {
                        Interlocked.Add(ref missingValueForEachmz, heatBins[rawIndex].Where(x => x == 0).Count());
                    }

                    heatBinDict.TryAdd(keyToSave, heatBins);
                    missingValueEachmzDict.TryAdd(keyToSave, missingValueForEachmz);
                }
                else // when the mz do not need to be combined, which means only one element
                {
                    int rawGroup = mzSelection[i].rawFileNumber.First();
                    string mz = mzSelection[i].distinctMZ;
                    heatBins[rawGroup] = MassDataInEachRawCollections[rawGroup][mz].ToArray();

                    Interlocked.Add(ref missingValueForEachmz,
                                    heatBins[rawGroup].Where(x => x == 0).Count() + (rawFileNumberTotal - 1) * colMax);

                    heatBinDict.TryAdd(keyToSave, heatBins);
                    missingValueEachmzDict.TryAdd(keyToSave, missingValueForEachmz);
                }
            });
            return (heatBinDict, missingValueEachmzDict);
        }


        /// <summary>
        /// heatBinDict, 1st one in the first item in the value is the row number, which means the .raw file number 
        /// </summary>
        /// <param name="rawFileNumberTotal"></param>
        /// <param name="colMax"></param>
        /// <param name="MassDataInEachRawCollections"></param>
        /// <returns></returns>
        public static ConcurrentDictionary<double, ConcurrentDictionary<int, double[]>> ProcessTheMzInAllTheRaw_v2(
            int rawFileNumberTotal, int colMax,
            ConcurrentDictionary<double, ConcurrentDictionary<int, double[]>> MassDataInEachRawCollections)
        {
            ConcurrentDictionary<double, ConcurrentDictionary<int, double[]>> MassDataInEachRawCollections2 = new();

            Parallel.ForEach(MassDataInEachRawCollections.Keys, keyToSave =>
            {
                int existingValues = 0;

                if (MassDataInEachRawCollections[keyToSave].Count() > rawFileNumberTotal * 0.2)
                {
                    for (int m = 0; m < MassDataInEachRawCollections[keyToSave].Count(); m++)
                    {
                        int nonZeroCount = MassDataInEachRawCollections[keyToSave].ElementAt(m).Value.Where(x => x != 0).Count();
                        Interlocked.Add(ref existingValues, nonZeroCount);
                    }

                    if (existingValues > rawFileNumberTotal * colMax * 0.2)
                    {
                        MassDataInEachRawCollections2.TryAdd(keyToSave, MassDataInEachRawCollections[keyToSave]);
                    }
                }
            });
            return MassDataInEachRawCollections2;
        }


        public static void CombineTheMzAndHeatbinInTheLoop(
            ConcurrentDictionary<double, ConcurrentDictionary<int, double[]>> heatBinFinal,
            ConcurrentDictionary<double, ConcurrentDictionary<int, double[]>> heatBinEach)
        {
            //Parallel.ForEach(heatBinEach, heatBinEach2 =>
            foreach (KeyValuePair<double, ConcurrentDictionary<int, double[]>> heatBinEach2 in heatBinEach)
            {
                if (heatBinFinal.ContainsKey(heatBinEach2.Key))
                {
                    for (int i = 0; i < heatBinEach2.Value.Count; i++)
                    {
                        int newRawNum = heatBinEach2.Value.ElementAt(i).Key;
                        heatBinFinal[heatBinEach2.Key].TryAdd(newRawNum, heatBinEach2.Value[newRawNum]);
                    }
                }
                else
                {
                    heatBinFinal.TryAdd(heatBinEach2.Key, heatBinEach2.Value);
                }
            }
        }

        public static SynchronizedCollection<string> GetDistinctMZInAllTheRawMRM(
            int rawFileNumber,
            List<ConcurrentDictionary<string, Tuple<string, List<double>>>> MassDataInEachRawCollectionsMRM,
            List<string> mZInEachRawCollections,
            int colMin)
        {
            var result = mZInEachRawCollections.GroupBy(s => s)
                .Select(g => new { distinctMZ = g.Key, count = g.Count() })
                .Where(g => g.count > rawFileNumber / 10).ToList();

            SynchronizedCollection<string> resultDistinctMzs = new();

            Parallel.ForEach(result, resultEach =>
            {
                int nullFrequency = 0;
                Parallel.ForEach(MassDataInEachRawCollectionsMRM, MassDataInEachRaw =>
                {
                    if (MassDataInEachRaw.ContainsKey(resultEach.distinctMZ))
                    {
                        Interlocked.Add(ref nullFrequency, MassDataInEachRaw[resultEach.distinctMZ].Item2.Take(colMin).Where(x => x == 0).Count());
                    }
                });
                Interlocked.Add(ref nullFrequency, (rawFileNumber - resultEach.count) * colMin);

                if (nullFrequency <= rawFileNumber * colMin * 0.9)
                {
                    // could add a excluded m/z list to export function
                    //nullIndex.Add(resultEach.distinctMZ);
                    resultDistinctMzs.Add(resultEach.distinctMZ);
                }
            });

            return resultDistinctMzs;
        }




        /// <summary>
        /// Gives the max and min (not zero) intensity of one m/z
        /// </summary>
        /// <param name="mZAndHeatbin"></param>
        /// <returns></returns>
        public static ConcurrentDictionary<string, double[]> ExtractMaxMinIntensity(
            ConcurrentDictionary<string, double[][]> mZAndHeatbin)
        {
            ConcurrentDictionary<string, double[]> mZAndMaxMinIntensity = new();

            foreach (KeyValuePair<string, double[][]> eachmZAndHeatbin in mZAndHeatbin)
            {
                double[] maxAndMin = new double[2];
                double maxHeat = 0;
                double minHeat = 10000000;

                for (int i = 0; i < eachmZAndHeatbin.Value.Length; i++)
                {
                    for (int j = 0; j < eachmZAndHeatbin.Value[i].Length; j++)
                    {
                        if (maxHeat < eachmZAndHeatbin.Value[i][j])
                        {
                            maxHeat = eachmZAndHeatbin.Value[i][j];
                        }

                        if (eachmZAndHeatbin.Value[i][j] != 0)
                        {
                            if (minHeat > eachmZAndHeatbin.Value[i][j])
                            {
                                minHeat = eachmZAndHeatbin.Value[i][j];
                            }
                        }
                    }
                }

                maxAndMin[0] = maxHeat;
                maxAndMin[1] = minHeat;

                mZAndMaxMinIntensity.TryAdd(eachmZAndHeatbin.Key, maxAndMin);
            }

            return mZAndMaxMinIntensity;
        }



        public static ConcurrentDictionary<double, double[]> ExtractMaxMinIntensity_v2(
            ConcurrentDictionary<double, ConcurrentDictionary<int, double[]>> mZAndHeatbin)
        {
            ConcurrentDictionary<double, double[]> mZAndMaxMinIntensity = new();

            foreach (KeyValuePair<double, ConcurrentDictionary<int, double[]>> eachmZAndHeatbin in mZAndHeatbin)
            {
                double[] maxAndMin = new double[2];
                double maxHeat = 0;
                double minHeat = 10000000;

                List<double> intensityList = eachmZAndHeatbin.Value.SelectMany(m => m.Value).ToList();

                foreach (double intensity in intensityList)
                {
                    if (maxHeat < intensity)
                    {
                        maxHeat = intensity;
                    }

                    if (minHeat > intensity)
                    {
                        minHeat = intensity;
                    }
                }

                maxAndMin[0] = maxHeat;
                maxAndMin[1] = minHeat;

                mZAndMaxMinIntensity.TryAdd(eachmZAndHeatbin.Key, maxAndMin);
            }

            return mZAndMaxMinIntensity;
        }



        /// <summary>
        /// prepare the heatmap for the selected m/z
        /// </summary>
        /// <param name="mzHeatbin"></param>
        /// <param name="rawFilesCount"></param>
        /// <param name="colNum"></param>
        /// <param name="theMaxInensity"></param>
        /// <returns></returns>
        public static Bitmap SingleMzAndHeatmap(double[][] mzHeatbin, int rawFilesCount, int colNum, double theMaxInensity, double originalToPictureBoxRatio)
        {
            Bitmap output = new(colNum, rawFilesCount, PixelFormat.Format32bppArgb);

            double[][] normalizedHeatbin = new double[rawFilesCount][];

            for (int i = 0; i < rawFilesCount; i++)
            {
                normalizedHeatbin[i] = new double[colNum];

                for (int j = 0; j < colNum; j++)
                {
                    normalizedHeatbin[i][j] = mzHeatbin[i][j] / theMaxInensity;
                    output.SetPixel(j, i, BasicColorMapping(normalizedHeatbin[i][j]));
                }
            }
            //Bitmap outputResized = new(output, new Size(output.Width, (int)Math.Ceiling(1.8 * output.Height)));
            Bitmap outputResized = new(output, new Size((int)(colNum), (int)(originalToPictureBoxRatio * rawFilesCount)));

            return outputResized;

        }



        public static Bitmap SingleMzAndHeatmap_v2(ConcurrentDictionary<int, double[]> mzHeatbin, int rawFilesCount, int colNum, double theMaxInensity, double originalToPictureBoxRatio)
        {
            Bitmap output = new(colNum, rawFilesCount, PixelFormat.Format32bppArgb);

            for (int i = 0; i < rawFilesCount; i++)
            {
                if (mzHeatbin.ContainsKey(i))
                {
                    for (int j = 0; j < colNum; j++)
                    {
                        output.SetPixel(j, i, BasicColorMapping(mzHeatbin[i][j] / theMaxInensity));
                    }
                }
                else
                {
                    for (int j = 0; j < colNum; j++)
                    {
                        output.SetPixel(j, i, BasicColorMapping(0));
                    }
                }
            }
            //Bitmap outputResized = new(output, new Size(output.Width, (int)Math.Ceiling(1.8 * output.Height)));
            Bitmap outputResized = new(output, new Size(colNum, (int)(originalToPictureBoxRatio * rawFilesCount)));

            return outputResized;

        }



        /// <summary>
        /// write the txt data for each m/z
        /// </summary>
        /// <param name="mZAndHeatbin"></param>
        /// <param name="mZAndMaxIntensity"></param>
        /// <param name="OutputDirectory"></param>
        /// <param name="writeCol"></param>
        /// <param name="rawFilesCount"></param>
        /// <param name="colNum"></param>
        public static void WriteAFAData(
            ConcurrentDictionary<string, double[][]> mZAndHeatbin,
            string OutputDirectory, string writeCol,
            int rawFilesCount, int colNum)
        {
            foreach (string mz in mZAndHeatbin.Keys)
            {
                string fileName = OutputDirectory + mz + ".txt";

                using (StreamWriter f = new(fileName))
                {
                    f.WriteLine(writeCol);

                    for (int i = 0; i < rawFilesCount; i++)
                    {
                        //string writeResult = mz.ToString("F4");
                        string writeResult = mz;
                        for (int j = 0; j < colNum; j++)
                        {
                            writeResult = writeResult + "\t" + mZAndHeatbin[mz][i][j]; // first is the column number
                        }
                        f.WriteLine(writeResult);
                    }
                }
            }
        }


        public static void WriteAFAData_v2(
           ConcurrentDictionary<double, ConcurrentDictionary<int, double[]>> mZAndHeatbin,
           string OutputDirectory, string writeCol,
           int rawFilesCount, int colNum)
        {
            //foreach(string mz in mZAndHeatbin.Keys)

            Parallel.ForEach(mZAndHeatbin.Keys, mz =>
            {
                string fileName = OutputDirectory + mz + ".txt";

                using (StreamWriter f = new(fileName))
                {
                    f.WriteLine(writeCol);

                    for (int i = 0; i < rawFilesCount; i++)
                    {
                        string writeResult = mz.ToString();
                        if (mZAndHeatbin[mz].ContainsKey(i))
                        {
                            for (int j = 0; j < colNum; j++)
                            {
                                double intensityVal = mZAndHeatbin[mz][i][j];
                                writeResult = writeResult + "\t" + intensityVal; // first is the column number
                            }
                        }
                        else
                        {
                            for (int j = 0; j < colNum; j++)
                            {
                                writeResult = writeResult + "\t" + 0;
                            }
                        }
                        f.WriteLine(writeResult);
                    }
                }
            });
        }



        /// <summary>
        /// Export the ROI data
        /// </summary>
        /// <param name="groupIntensityDict"></param>
        /// <param name="writeCol1"></param>
        /// <param name="writeCol2"></param>
        /// <param name="fileName"></param>
        public static void WriteROIData(ConcurrentDictionary<double, double[]> groupIntensityDict, string writeCol1, string writeCol2, string fileName)
        {

            using StreamWriter f = new(fileName);
            f.WriteLine(writeCol1);
            f.WriteLine(writeCol2);
            foreach (KeyValuePair<double, double[]> mz in groupIntensityDict)
            {
                // write the ROI data in a txt file
                string writeResult = mz.Key.ToString();
                for (int i = 0; i < mz.Value.Length; i++)
                {
                    writeResult = writeResult + "\t" + mz.Value[i];
                }
                f.WriteLine(writeResult);
            }
        }

        // the heatmap code was inspired with https://github.com/NLilley/Heatmap/blob/master/HeatMask.cs
        public static Color BasicColorMapping(double f)
        {
            return f switch
            {
                < 0.03 => Color.FromArgb(255, 0, 0, 0),
                < 0.1 => Color.FromArgb(255, 0, (int)(6 * (1 / 0.07) * (f - 0.03)), (int)(198 * (1 / 0.07) * (f - 0.03))),
                < 0.2 => Color.FromArgb(255, 0, (int)(6 + 83 * 10 * (f - 0.1)), (int)(198 + 57 * 10 * (f - 0.1))),
                < 0.4 => Color.FromArgb(255, 0, (int)(255 * 5 * (f - 0.2)), 255),
                < 0.5 => Color.FromArgb(255, 0, 255, (int)(255 * 10 * (0.5 - f))),
                < 0.6 => Color.FromArgb(255, (int)(255 * 10 * (f - 0.5)), 255, 0),
                < 0.8 => Color.FromArgb(255, 255, (int)(255 * 0.5 * (0.8 - f)), 0),
                <= 1 => Color.FromArgb(255, (int)(255 * 0.5 * (1 - f)), 0, 0),
                _ => Color.FromArgb(255, 0, 6, 198)
            };
        }

        public static ConcurrentDictionary<string, string[]> GetRawFileNames(string[] fileLocations)
        {
            ConcurrentDictionary<string, string[]> slicesAndRawFileNames = new();
            for (int j = 0; j < fileLocations.Length; j++)
            {
                List<string> rawFilesInEachSlices2 = Directory.GetFiles(fileLocations[j], "*.raw", System.IO.SearchOption.TopDirectoryOnly).ToList();
                var myComparer = new CustomComparer();
                rawFilesInEachSlices2.Sort(myComparer);
                string[] rawFilesInEachSlices = rawFilesInEachSlices2.ToArray();

                string shortRawFileNameInEachSlices = fileLocations[j].Substring(fileLocations[j].LastIndexOf(@"\") + 1);
                slicesAndRawFileNames.TryAdd(shortRawFileNameInEachSlices, rawFilesInEachSlices);
            }
            return slicesAndRawFileNames;
        }

        public static ConcurrentDictionary<string, string[]> GetmzXMLFileNames(string[] fileLocations)
        {
            ConcurrentDictionary<string, string[]> slicesAndRawFileNames = new();
            for (int j = 0; j < fileLocations.Length; j++)
            {
                List<string> rawFilesInEachSlices2 = Directory.GetFiles(fileLocations[j], "*.mzXML", SearchOption.TopDirectoryOnly).ToList();
                var myComparer = new CustomComparer();
                rawFilesInEachSlices2.Sort(myComparer);
                string[] rawFilesInEachSlices = rawFilesInEachSlices2.ToArray();

                string shortRawFileNameInEachSlices = fileLocations[j].Substring(fileLocations[j].LastIndexOf(@"\") + 1);
                slicesAndRawFileNames.TryAdd(shortRawFileNameInEachSlices, rawFilesInEachSlices);
            }
            return slicesAndRawFileNames;
        }


        public static double FromRectangleToData(ConcurrentDictionary<string, List<Rectangle>> rects,
            bool widerOrHigher, string sliceName, PictureBox pictureBoxTab2,
            double stretchRatio, double originalToPictureBoxRatio,
            ConcurrentDictionary<string, int> rawFileNumberDict,
            ConcurrentDictionary<string, int> colNumDict,
            KeyValuePair<string, double[][]> mZ)
        {
            double[] AvgValue = new double[rects[sliceName].Count];
            for (int z = 0; z < rects[sliceName].Count; z++)
            {
                Rectangle Rect = rects[sliceName][z];

                if (widerOrHigher)
                {
                    int startYpoint = (int)((Rect.Location.Y - pictureBoxTab2.Height / 2) * stretchRatio / originalToPictureBoxRatio + rawFileNumberDict[sliceName] / 2);
                    int endYpoint = (int)((Rect.Location.Y + Rect.Height - pictureBoxTab2.Height / 2) * stretchRatio / originalToPictureBoxRatio + rawFileNumberDict[sliceName] / 2);
                    for (int i = startYpoint; i < endYpoint; i++)
                    {
                        for (int j = (int)(Rect.Location.X * stretchRatio);
                            j < (int)((Rect.Left + Rect.Width) * stretchRatio); j++)
                        {
                            AvgValue[z] += mZ.Value[i][j];
                        }
                    }
                }
                else
                {
                    int startXpoint = (int)((Rect.Location.X - pictureBoxTab2.Width / 2) * stretchRatio + colNumDict[sliceName] / 2);
                    int endXpoint = (int)((Rect.Location.X + Rect.Width - pictureBoxTab2.Width / 2) * stretchRatio + colNumDict[sliceName] / 2);
                    for (int i = (int)(Rect.Location.Y * stretchRatio / originalToPictureBoxRatio);
                        i < (int)((Rect.Location.Y + Rect.Height) * stretchRatio / originalToPictureBoxRatio);
                        i++)
                    {
                        for (int j = startXpoint; j < endXpoint; j++)
                        {
                            AvgValue[z] += mZ.Value[i][j];
                        }
                    }
                }
                //doesnot matter about the number of cells to average since the selected ROI rectangles have equal size
                //AvgValue[z] = AvgValue[z] / Rect.Height * Rect.Width * stretchRatioY * stretchRatioX;
            }
            return AvgValue.Sum() / AvgValue.Length;
        }



        public static double FromRectangleToData_v2(ConcurrentDictionary<string, List<Rectangle>> rects,
            bool widerOrHigher, string sliceName, PictureBox pictureBoxTab2,
            double stretchRatio, double originalToPictureBoxRatio,
            ConcurrentDictionary<string, int> rawFileNumberDict,
            ConcurrentDictionary<string, int> colNumDict,
             ConcurrentDictionary<int, double[]> mZIntensity)
        {
            double[] AvgValue = new double[rects[sliceName].Count];
            for (int z = 0; z < rects[sliceName].Count; z++)
            {
                Rectangle Rect = rects[sliceName][z];
                AvgValue[z] += SumTheIntensity(mZIntensity, pictureBoxTab2, originalToPictureBoxRatio, rawFileNumberDict[sliceName],
                    colNumDict[sliceName], stretchRatio, Rect, widerOrHigher);
                //doesnot matter about the number of cells to average since the selected ROI rectangles have equal size
                //AvgValue[z] = AvgValue[z] / Rect.Height * Rect.Width * stretchRatioY * stretchRatioX;
            }
            return AvgValue.Sum() / AvgValue.Length;
        }

        public static double SumTheIntensity(
            ConcurrentDictionary<int, double[]> mZIntensity, PictureBox pictureBoxTab2, double originalToPictureBoxRatio, 
            int rawFilesNum, int colNum,
            double stretchRatio, Rectangle Rect, bool widerOrHigher)
        {
            double ROIintensity = 0;

            int startYpoint;
            int endYpoint;
            int startXpoint;
            int endXpoint;

            if (widerOrHigher)
            {
                startYpoint = (int)((Rect.Location.Y - pictureBoxTab2.Height / 2) * stretchRatio / originalToPictureBoxRatio + rawFilesNum / 2);
                endYpoint = (int)((Rect.Location.Y + Rect.Height - pictureBoxTab2.Height / 2) * stretchRatio / originalToPictureBoxRatio + rawFilesNum / 2);
                startXpoint = (int)(Rect.Location.X * stretchRatio);
                endXpoint = (int)((Rect.Left + Rect.Width) * stretchRatio);

            }
            else
            {
                startYpoint = (int)(Rect.Location.Y * stretchRatio / originalToPictureBoxRatio);
                endYpoint = (int)((Rect.Location.Y + Rect.Height) * stretchRatio / originalToPictureBoxRatio);
                startXpoint = (int)((Rect.Location.X - pictureBoxTab2.Width / 2) * stretchRatio + colNum / 2);
                endXpoint = (int)((Rect.Location.X + Rect.Width - pictureBoxTab2.Width / 2) * stretchRatio + colNum / 2);
            }

            for (int i = startYpoint; i < endYpoint; i++)
            {
                if (mZIntensity.ContainsKey(i))
                {
                    for (int j = startXpoint; j < endXpoint; j++)
                    {
                        ROIintensity += mZIntensity[i][j];
                    }
                }                
            }
            return ROIintensity;
        }

        
        /// <summary>
        /// https://stackoverflow.com/questions/7560760/cosine-similarity-code-non-term-vectors
        /// </summary>
        /// <param name="V1"></param>
        /// <param name="V2"></param>
        /// <returns></returns>
        public static double CalculateConsine(List<double> V1, List<double> V2)
        {
            int N = V2.Count < V1.Count ? V2.Count : V1.Count;
            double dot = 0.0d;
            double mag1 = 0.0d;
            double mag2 = 0.0d;
            for (int n = 0; n < N; n++)
            {
                dot += V1[n] * V2[n];
                mag1 += Math.Pow(V1[n], 2);
                mag2 += Math.Pow(V2[n], 2);
            }
            return dot / (Math.Sqrt(mag1) * Math.Sqrt(mag2));
        }

        public static ConcurrentDictionary<string, List<double>> AIFDictFilter(ConcurrentDictionary<string, List<double>> AIF_mz_data)
        {
            ConcurrentDictionary<string, List<double>> newAIF_mz_data = new();
            for (int i = 0; i < AIF_mz_data.Count; i++)
            {
                KeyValuePair<string, List<double>> AIF_ = AIF_mz_data.ElementAt(i);
                double zeroPercentage = (double)AIF_.Value.Where(x => x == 0).Count() / AIF_.Value.Count;
                bool intensityThreshold = AIF_.Value.Average() > 3000;
                if (zeroPercentage < 0.8 && intensityThreshold)
                {
                    newAIF_mz_data.TryAdd(AIF_.Key, AIF_.Value);
                }
            }
            return newAIF_mz_data;
        }

        /// <summary>
        /// After obtaining all the precursor and daughter ions, to process the data, calculate the cosine and take the first 20.
        /// </summary>
        /// <param name="mz_data_precursor"></param>
        /// <param name="mz_data_ionized"></param>
        /// <returns></returns>
        public static ConcurrentDictionary<string, ConcurrentDictionary<string, double>> CalculateCosineInTwoDDMS2Dict(ConcurrentDictionary<string, List<double>> mz_data_precursor,
                                            ConcurrentDictionary<string, List<double>> mz_data_ionized)
        {
            mz_data_precursor = AIFDictFilter(mz_data_precursor);
            mz_data_ionized = AIFDictFilter(mz_data_ionized);

            // the first key is the precursor and the second is the daughter ion
            ConcurrentDictionary<string, ConcurrentDictionary<string, double>> res = new();

            Parallel.For(0, mz_data_precursor.Count, i =>
            //for (int i = 0; i < mz_data_precursor.Count; i++)
            {
                ConcurrentDictionary<string, double> res2 = new();

                for (int j = 0; j < mz_data_ionized.Count(); j++)
                {
                    if (double.Parse(mz_data_precursor.ElementAt(i).Key) > double.Parse(mz_data_ionized.ElementAt(j).Key))
                    {
                        double cosineValue = CalculateConsine(mz_data_precursor.ElementAt(i).Value, mz_data_ionized.ElementAt(j).Value);
                        res2.TryAdd(mz_data_ionized.ElementAt(j).Key, cosineValue);
                    }
                }

                //IEnumerable<KeyValuePair<string, List<double>>> mz_data_ionized2 = mz_data_ionized.Where(x => double.Parse(x.Key) < double.Parse(mz_data_precursor.ElementAt(i).Key));
                //for (int j = 0; j < mz_data_ionized2.Count(); j++)
                //{
                //    double cosineValue = CalculateConsine(mz_data_precursor.ElementAt(i).Value, mz_data_ionized2.ElementAt(j).Value);
                //    res2.TryAdd(mz_data_ionized2.ElementAt(j).Key, cosineValue);
                //}
                ConcurrentDictionary<string, double> res3 = res2.OrderBy(x => x.Value).Reverse().Take(20).ToConcurrentDictionary();
                res.TryAdd(mz_data_precursor.ElementAt(i).Key, res3);
            });
            return res;
        }


        /// <summary>
        /// calculate the daughter ions with 80% appearence in all the .raw
        /// </summary>
        /// <param name="processedData"></param>
        /// <returns></returns>
        public static ConcurrentDictionary<string, Tuple<IEnumerable<string>, IEnumerable<double>>> PrepareTheDict(ConcurrentDictionary<string, Tuple<List<ConcurrentDictionary<string, double>>, List<int>>> processedData)
        {
            var mzSelection = processedData
                .Select(eachPrecursor => new
                {
                    eachPrecursorIon = eachPrecursor.Key,
                    daughterIons = eachPrecursor.Value.Item1.SelectMany(m => m.Keys).GroupBy(t => t).Select(t => t.Key),
                    indexInRawRange = eachPrecursor.Value.Item2.Max() - eachPrecursor.Value.Item2.Min(),
                    daughterIntensityDict = eachPrecursor.Value.Item1.SelectMany(m => m)
                    .GroupBy(m2 => m2.Key).Select(m3 => new
                    {
                        average = m3.Average(m4 => m4.Value)
                    })
                })
                .Where(x => x.daughterIons.Count() > (x.indexInRawRange * 0.8))
                .Select(x => new
                {
                    x.eachPrecursorIon,
                    daughterIonsArray = x.daughterIons,
                    intensity = x.daughterIntensityDict.Select(t => t.average)
                })
                .ToConcurrentDictionary(x => x.eachPrecursorIon, x => (x.daughterIonsArray, x.intensity).ToTuple());

            return mzSelection;
        }



        public static REngine? engine;

        public static void ReadInFromFiles(
            string folderName, string[] rawFilesinEachSlice,
            ConcurrentDictionary<double, ConcurrentDictionary<int, double[]>> mZAndHeatbin,
            ConcurrentDictionary<string, int> colNumDict,
            ConcurrentDictionary<string, int> rawFileNumberDict,
            ConcurrentDictionary<string, double> originalToPictureBoxRatioDict,
            double lower, double upper)
        {
            int colNum = 0;
            int rowNum = 0;
            double intervalTime = 0.0;
            double ratio = 0.0;
            string[] rawFiles = rawFilesinEachSlice.OrderBy(x => x).ToArray();
            string MSIFileFormat = rawFiles.First()[(rawFiles.First().IndexOf(".") + 1)..];

            REngine.SetEnvironmentVariables();
            engine = REngine.GetInstance();
            engine.Evaluate("Sys.setenv(PATH = paste(\"C:/Program Files/R/R-4.2.1/bin/x64\", Sys.getenv(\"PATH\"), sep=\";\"))");
            ConcurrentDictionary<double, ConcurrentDictionary<int, double[]>> mz_data = new();
            if (MSIFileFormat == "raw")
            {
                //use the first scan row as the reference of the scans reference, final time and interval time
                IRawFileThreadManager rawFileFirst = RawFileReaderFactory.CreateThreadManager(rawFiles.First());
                var staticRawFile = rawFileFirst.CreateThreadAccessor();
                staticRawFile.SelectInstrument(ThermoFisher.CommonCore.Data.Business.Device.MS, 1);
                int[] scansReference = staticRawFile.GetFilteredScanEnumerator(staticRawFile.GetFilterFromString("")).ToArray().OrderBy(i => i).ToArray(); // get all scans
                double finalTime = staticRawFile.RetentionTimeFromScanNumber(scansReference.Last());

                colNum = scansReference.Length;
                intervalTime = finalTime / (scansReference.Last() - 1);
                rowNum = rawFiles.Length;
                // calculate the actual width versus height ratio (each pixel 0.25 mm, 0.2 mm/s)
                ratio = 0.25 / (intervalTime * 60 * 0.2);

                //Parallel.ForEach(rawFiles, file =>
                foreach (string file in rawFiles)
                {
                    //string file = rawFiles[i];
                    //singleFileTime.Start();
                    //ThreadHelperClass.SetText(this, textBox1Tab1, "Processing: " + file + " in the " + rawFilesinEachSlice.Key + " slice directory." + newLine);
                    #region MRM region, edit for later
                    //if (checkBoxTab1MRMdata.Checked)
                    //{
                    //using (IRawFileThreadManager rawFile = RawFileReaderFactory.CreateThreadManager(file)) // BB: using make sure the correct use of IDisposable objects
                    //{
                    //    //for the 1st version MRM read-in
                    //    //ConcurrentDictionary<string, List<double>> mz_data = CollectRawData.ReadInFromRawMRM(rawFile);
                    //    //MassDataInEachRawCollections.TryAdd(i, mz_data);
                    //    //colNum = mz_data.ElementAt(1).Value.Count;
                    //    //for the DDMS2 read-in
                    //    //ConcurrentDictionary<int, List<double[]>> mz_data = CollectRawData.ReadInFromRawMRM2(rawFile);
                    //    //MassDataInEachRawCollections2.TryAdd(i, mz_data);
                    //    //for the AIF data read-in

                    //    (ConcurrentDictionary<string, List<double>> mz_data_precursor,
                    //    ConcurrentDictionary<string, List<double>> mz_data_ionized) = CollectRawData.ReadInFromRawMRM3(rawFile);

                    //    ConcurrentDictionary<string, ConcurrentDictionary<string, double>> compareResult = AFAMassIntensityPipeline.CalculateCosineInTwoDDMS2Dict(mz_data_precursor, mz_data_ionized);

                    //    for (int j = 0; j < compareResult.Count; j++)
                    //    {
                    //        string precursor = compareResult.ElementAt(j).Key;

                    //        if (resultDictAIF.ContainsKey(precursor))
                    //        {
                    //            resultDictAIF[precursor].Item1.Add(compareResult[precursor]);
                    //            resultDictAIF[precursor].Item2.Add(i);
                    //        }
                    //        else
                    //        {
                    //            List<int> appearingIndexList = new() { i };
                    //            List<ConcurrentDictionary<string, double>> newDict = new List<ConcurrentDictionary<string, double>>() { compareResult[precursor] };
                    //            resultDictAIF.TryAdd(precursor, (newDict, appearingIndexList).ToTuple());
                    //        }
                    //    }

                    //    //using (StreamWriter f = new(("D:/desktop/AIF_dic_No" + file.Substring(file.LastIndexOf(@"\")) + ".txt").Replace(@"\", @"")))
                    //    //{
                    //    //    f.WriteLine("precursor" + "\t" + "ionized" + "\t" + "cosineValue" + "\t" + "precursorValue" + "\t" + "ionizedValue");
                    //    //    foreach (KeyValuePair<string, ConcurrentDictionary<string, double>> precursorAndIonized in compareResult)
                    //    //    {
                    //    //        List<KeyValuePair<string, double>> ionizedAndCosineList = precursorAndIonized.Value.OrderBy(x => x.Value).Reverse().Take(20).ToList();
                    //    //        foreach (KeyValuePair<string, double> ionizedAndCosine in ionizedAndCosineList)
                    //    //        {
                    //    //            string writeOut = "";
                    //    //            foreach (double eachValue in mz_data_precursor[precursorAndIonized.Key])
                    //    //            {
                    //    //                writeOut = writeOut + eachValue + ", ";
                    //    //            }
                    //    //            string writeOut2 = "";
                    //    //            foreach (double eachValue in mz_data_ionized[ionizedAndCosine.Key])
                    //    //            {
                    //    //                writeOut2 = writeOut2 + eachValue + ", ";
                    //    //            }
                    //    //            f.WriteLine(precursorAndIonized.Key + "\t" + ionizedAndCosine.Key + "\t" + ionizedAndCosine.Value + "\t" +
                    //    //                writeOut + "\t" + writeOut2);
                    //    //        };
                    //    //    }
                    //    //}
                    //} 
                    //}
                    #endregion
                    //else
                    //{
                    using (IRawFileThreadManager rawFile = RawFileReaderFactory.CreateThreadManager(file)) // BB: using make sure the correct use of IDisposable objects
                    {
                        mz_data = CollectRawData.ReadInFromRaw_2(rawFile, scansReference, intervalTime, rawFiles.ToList().IndexOf(file), lower, upper);
                        CombineTheMzAndHeatbinInTheLoop(mZAndHeatbin, mz_data);
                        mz_data.Clear();
                    }
                    //}
                    //singleFileTime.Stop();
                    //ThreadHelperClass.SetText(this, textBox1Tab1, "Elapsed time: " + Math.Round(Convert.ToDouble(singleFileTime.ElapsedMilliseconds) / 1000.0, 2) + "s" + newLine);
                    //singleFileTime.Reset();
                }
            }

            if (MSIFileFormat == "mzXML")
            {
                var readerFirstXML = new MzXMLFileReader();
                readerFirstXML.OpenFile(rawFiles.First());

                readerFirstXML.ReadNextSpectrum(out SpectrumInfo specturmInfo);
                colNum = readerFirstXML.ScanCount;
                intervalTime = (readerFirstXML.FileInfoEndTimeMin - readerFirstXML.FileInfoStartTimeMin) / (colNum - 1);
                rowNum = rawFiles.Length;
                ratio = 0.25 / (intervalTime * 60 * 0.2);

                for (int i = 0; i < rawFiles.Length; i++) // file is a string with full directory
                //Parallel.ForEach(rawFiles, rawFile =>
                {
                    var readerEachXML = new MzXMLFileReader();
                    readerEachXML.OpenFile(rawFiles[i]);

                    List<double> retentionTimeList = new();
                    List<SpectrumInfo> spectraList = new();

                    // each .raw file
                    while (readerEachXML.ReadNextSpectrum(out SpectrumInfo spectrumInfo))
                    {
                        if (spectrumInfo == null)
                        {
                            continue;
                        }
                        retentionTimeList.Add(spectrumInfo.RetentionTimeMin);
                        spectraList.Add(spectrumInfo);
                    }

                    int scansLength = readerEachXML.ScanCount;

                    mz_data = AFACentroidCollectionToList.CentroidCollectionDatamzXML(spectraList, colNum, intervalTime, retentionTimeList.ToArray(), i, lower, upper);
                    CombineTheMzAndHeatbinInTheLoop(mZAndHeatbin, mz_data);
                    mz_data.Clear();
                }
            }

            if (MSIFileFormat == "imzML" | MSIFileFormat == "ibd")
            {
                string imzMLFile = rawFiles.Where(x => x.Contains(".imzML")).First();
                (colNum, rowNum) = CollectRawData.ReadInFromimzML(imzMLFile, engine, mZAndHeatbin, lower, upper);
                ratio = 1.8;
            }

            colNumDict.TryAdd(folderName, colNum);
            rawFileNumberDict.TryAdd(folderName, rowNum);
            originalToPictureBoxRatioDict.TryAdd(folderName, ratio);
        }


        public static List<int> CalculateScanNumbers(string[] rawFilesinEachSlice)
        {
            string[] rawFiles = rawFilesinEachSlice;
            string MSIFileFormat = rawFiles.First()[(rawFiles.First().IndexOf(".") + 1)..];
            List<int> scanNumberFluctuation = new();

            if (MSIFileFormat == "raw")
            {
                Parallel.ForEach(rawFiles, file =>
                {
                    var rawFile = RawFileReaderFactory.CreateThreadManager(file).CreateThreadAccessor();
                    rawFile.SelectInstrument(ThermoFisher.CommonCore.Data.Business.Device.MS, 1);
                    int scanNum = rawFile.GetFilteredScanEnumerator(rawFile.GetFilterFromString("")).Count();
                    scanNumberFluctuation.Add(scanNum);
                });
            }

            if (MSIFileFormat == "mzXML")
            {
                Parallel.ForEach(rawFiles, file =>
                {
                    var readerEachXML = new MzXMLFileReader();
                    readerEachXML.OpenFile(file);
                    readerEachXML.ReadAndCacheEntireFile();
                    scanNumberFluctuation.Add(readerEachXML.ScanCount);
                });
            }

            return scanNumberFluctuation;
        }

        public static void GetTheFiles(string[] fileLocations, ConcurrentDictionary<string, string[]> slicesAndRawFileNames)
        {
            var myComparer = new CustomComparer();
            for (int j = 0; j < fileLocations.Length; j++)
            {
                if (Directory.GetFiles(fileLocations[j]).Length != 0)
                {
                    string firstFile = Directory.GetFiles(fileLocations[j]).ToList().First();

                    string fileFormat = firstFile.Substring(firstFile.IndexOf(".") + 1);
                    if (fileFormat == "raw")
                    {
                        List<string> rawFilesInEachSlices2 = Directory.GetFiles(fileLocations[j], "*.raw", System.IO.SearchOption.TopDirectoryOnly).ToList();
                        rawFilesInEachSlices2.Sort(myComparer);
                        string[] rawFilesInEachSlices = rawFilesInEachSlices2.ToArray();

                        string shortRawFileNameInEachSlices = fileLocations[j].Substring(fileLocations[j].LastIndexOf(@"\") + 1);
                        slicesAndRawFileNames.TryAdd(shortRawFileNameInEachSlices, rawFilesInEachSlices);
                    }
                    if (fileFormat == "imzML" | fileFormat == "ibd")
                    {
                        List<string> rawFilesInEachSlicesimzML = Directory.GetFiles(fileLocations[j], "*.imzML", SearchOption.TopDirectoryOnly).ToList();
                        List<string> rawFilesInEachSlicesibd = Directory.GetFiles(fileLocations[j], "*.ibd", SearchOption.TopDirectoryOnly).ToList();
                        if (rawFilesInEachSlicesimzML.Count != 1) { MessageBox.Show("No imzML files found in " + fileLocations[j] + "."); }
                        if (rawFilesInEachSlicesibd.Count != 1) { MessageBox.Show("No ibd files found in " + fileLocations[j] + "."); }
                        string imzMLFile = rawFilesInEachSlicesimzML.First();
                        string ibdFile = rawFilesInEachSlicesibd.First();
                        string[] valueToAdd = new string[] { imzMLFile, ibdFile };
                        string shortRawFileNameInEachSlices = imzMLFile[(imzMLFile.LastIndexOf(@"\") + 1)..];
                        slicesAndRawFileNames.TryAdd(shortRawFileNameInEachSlices, valueToAdd);
                    }

                    if (fileFormat == "mzXML")
                    {
                        List<string> rawFilesInEachSlices2 = Directory.GetFiles(fileLocations[j], "*.mzXML", SearchOption.TopDirectoryOnly).ToList();
                        rawFilesInEachSlices2.Sort(myComparer);
                        string[] rawFilesInEachSlices = rawFilesInEachSlices2.ToArray();

                        string shortRawFileNameInEachSlices = fileLocations[j].Substring(fileLocations[j].LastIndexOf(@"\") + 1);
                        slicesAndRawFileNames.TryAdd(shortRawFileNameInEachSlices, rawFilesInEachSlices);
                    }
                }
            }
        }


        public static void GatherEachIntensityAndMz(ConcurrentDictionary<double, ConcurrentDictionary<int, double[]>> mzData, int row, int col, double mZ, double intensity, int scansReferenceLength)
        {
            if (mzData.ContainsKey(mZ))
            {
                if (mzData[mZ].ContainsKey(row))
                {
                    if (mzData[mZ][row][col] == 0)
                    {
                        mzData[mZ][row][col] = intensity;
                    }
                    else
                    {
                        mzData[mZ][row][col] = (mzData[mZ][row][col] + intensity) / 2;
                    }
                }
                else
                {
                    double[] newElementDict = new double[scansReferenceLength];
                    newElementDict[col] = intensity;
                    mzData[mZ].TryAdd(row, newElementDict);
                }
            }
            else
            {
                ConcurrentDictionary<int, double[]> newElement = new();
                double[] newElementDict = new double[scansReferenceLength];
                newElementDict[col] = intensity;
                newElement.TryAdd(row, newElementDict);
                mzData.TryAdd(mZ, newElement);
            }
        }

        /// <summary>
        /// col is zero based
        /// </summary>
        /// <param name="col"></param>
        /// <param name="row"></param>
        /// <param name="intensityList"></param>
        /// <param name="mzList"></param>
        /// <param name="mzData"></param>
        public static void GatheringTheSpectrum(int col, int row, IEnumerable<double> intensityList, IEnumerable<double> mzList,
            ConcurrentDictionary<double, ConcurrentDictionary<int, double[]>> mzData, int scansReferenceLength)
        {
            Parallel.For(0, intensityList.Count() - 1, j =>
            {
                double mZ = Math.Round(AFACentroidCollectionToList.ProcessTheMz(mzList.ElementAt(j)), 4); //mz could be duplicated after this step
                double intensity = intensityList.ElementAt(j);
                GatherEachIntensityAndMz(mzData, row, col, mZ, intensity, scansReferenceLength);
            });
        }

        public static void GatheringTheSpectrum(int col, int row, IEnumerable<double> intensityList, IEnumerable<double> mzList,
            ConcurrentDictionary<double, ConcurrentDictionary<int, double[]>> mzData, double lower, double upper, int scansReferenceLength)
        {
            //Parallel.For(0, intensityList.Count() - 1, j =>
            for (int j = 0; j < intensityList.Count(); j++)
            {
                if (mzList.ElementAt(j) > lower & mzList.ElementAt(j) < upper)
                {
                    double mZ = Math.Round(AFACentroidCollectionToList.ProcessTheMz(mzList.ElementAt(j)), 4); //mz could be duplicated after this step
                    double intensity = intensityList.ElementAt(j);
                    GatherEachIntensityAndMz(mzData, row, col, mZ, intensity, scansReferenceLength);
                }
            }
        }

        public static Rectangle RepositionTheRects(bool widerOrHigher, Rectangle Rect, PictureBox pictureBoxTab2, int pictureHeight, int pictureWidth, double stretchRatio)
        {
            if (widerOrHigher)
            {
                if (Rect.Bottom > pictureBoxTab2.Height / 2 + (int)(pictureHeight / stretchRatio) / 2)
                {
                    Rect.Offset(0, -(Rect.Bottom - pictureBoxTab2.Height / 2 - (int)(pictureHeight / stretchRatio) / 2));
                }
                if (Rect.Top < pictureBoxTab2.Height / 2 - (int)(pictureHeight / stretchRatio) / 2)
                {
                    Rect.Offset(0, pictureBoxTab2.Height / 2 - (int)(pictureHeight / stretchRatio) / 2 - Rect.Top);
                }
                if (Rect.Right > pictureBoxTab2.Width)
                {
                    Rect.Offset(pictureBoxTab2.Width - Rect.Right, 0);
                }
            }
            else
            {
                if (Rect.Right > pictureBoxTab2.Width / 2 + (int)(pictureWidth / stretchRatio) / 2)
                {
                    Rect.Offset(-(Rect.Right - pictureBoxTab2.Width / 2 - (int)(pictureWidth / stretchRatio) / 2), 0);
                }
                if (Rect.Left < pictureBoxTab2.Width / 2 - (int)(pictureWidth / stretchRatio) / 2)
                {
                    Rect.Offset(pictureBoxTab2.Width / 2 - (int)(pictureWidth / stretchRatio) / 2 - Rect.Left, 0);
                }
                if (Rect.Bottom > pictureBoxTab2.Height)
                {
                    Rect.Offset(0, -(Rect.Bottom - pictureBoxTab2.Height));
                }
            }
            return Rect;
        }
    }
}
