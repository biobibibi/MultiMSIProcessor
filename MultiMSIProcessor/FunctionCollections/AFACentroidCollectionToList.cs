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
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using MSDataFileReader;
using ThermoFisher.CommonCore.Data.Business;
using ThermoFisher.CommonCore.Data.Interfaces;

namespace MultiMSIProcessor.FunctionCollections
{
    internal static class AFACentroidCollectionToList
    {
        /// <summary>
        /// make a dictionary where m/z is the key, and a list with the intensity in each scan as the value.
        /// the m/z underwent alignment using the self-made method ProcessTheMz.
        /// </summary>
        /// <param name="rawFile"></param>
        /// <returns></returns>
        public static ConcurrentDictionary<string, List<double>> CentroidCollectionData(IRawDataExtended rawFile, int[] scansReference, double intervalTime)
        {
            rawFile.SelectInstrument(Device.MS, 1);

            ConcurrentDictionary<string, List<double>> mz_data = new();

            for (int i = 0; i < scansReference.Length; i++) // scan = 1 2 3 ...
            {
                int targetScan = rawFile.ScanNumberFromRetentionTime(i * intervalTime);

                CentroidStream centroid = rawFile.GetCentroidStream(targetScan, false);

                for (int j = 0; j < centroid.Masses.Length; j++)
                {
                    // give the initial value 0 to each scan of mz
                    List<double> intensity_list = Enumerable.Repeat((double)0, scansReference.Length).ToList();
                    string mZ = Math.Round(ProcessTheMz(centroid.Masses[j]), 4).ToString(); //mz could be duplicated after this step
                    double intensity = centroid.Intensities[j];

                    if (!mz_data.ContainsKey(mZ))
                    {
                        mz_data.TryAdd(mZ, intensity_list);
                        mz_data[mZ][i] = intensity;
                    }
                    else //if the mz does exist
                    {
                        //if the mz intensity is 0, which means the initial value, then give the intensity
                        if (mz_data[mZ][i] == 0)
                        {
                            mz_data[mZ][i] = intensity;
                        }
                        //if the mz duplicates, then the intensity would take average
                        else
                        {
                            mz_data[mZ][i] = (mz_data[mZ][i] + intensity) / 2;
                        }
                    }
                }
            }
            return mz_data;
        }

        /// <summary>
        /// change the logic from the whole picture to only the position
        /// </summary>
        /// <param name="rawFile"></param>
        /// <param name="scansReference"></param>
        /// <param name="intervalTime"></param>
        /// <returns></returns>
        public static ConcurrentDictionary<double, ConcurrentDictionary<int, double[]>> CentroidCollectionData_v2(
            IRawDataExtended rawFile, int[] scansReference, double intervalTime, int rawFileNumber, double lower, double upper)
        {
            rawFile.SelectInstrument(Device.MS, 1);

            ConcurrentDictionary<double, ConcurrentDictionary<int, double[]>> mz_data = new();

            for (int i = 0; i < scansReference.Length; i++) // scan = 1 2 3 ...
            {
                int targetScan = rawFile.ScanNumberFromRetentionTime(i * intervalTime);

                CentroidStream centroid = rawFile.GetCentroidStream(targetScan, false);

                if (lower != 0 & upper != 1000000.0)
                {
                    AFAMassIntensityPipeline.GatheringTheSpectrum(i, rawFileNumber, centroid.Intensities, centroid.Masses, mz_data, lower, upper, scansReference.Length);
                }
                else
                {
                    AFAMassIntensityPipeline.GatheringTheSpectrum(i, rawFileNumber, centroid.Intensities, centroid.Masses, mz_data, scansReference.Length);
                }

            }
            return mz_data;
        }


        /// <summary>
        /// 1st version MRM data
        /// </summary>
        /// <param name="rawFile"></param>
        /// <returns></returns>
        public static ConcurrentDictionary<string, List<double>> CentroidCollectionDataMRM_v1(IRawDataExtended rawFile)
        {

            rawFile.SelectInstrument(Device.MS, 1);
            //all scans are used since it is a MSI data
            int[] scans = rawFile.GetFilteredScanEnumerator(rawFile.GetFilterFromString("")).ToArray(); // get all scans

            ConcurrentDictionary<string, List<double>> massIntensityResults =
                new ConcurrentDictionary<string, List<double>>();

            foreach (int scan in scans)
            {
                IScanEvent scanEvent = rawFile.GetScanEventForScanNumber(scan);

                double mass = Math.Round(scanEvent.GetMassRange(0).High, 2);
                string nameChemical = scanEvent.Name;
                SegmentedScan segment = rawFile.GetSegmentedScanFromScanNumber(scan, null);
                double intensity = segment.Intensities[0];

                double precursor = Math.Round(scanEvent.GetReaction(0).PrecursorMass, 4);

                string precursorAndMass = precursor.ToString() + "-" + mass.ToString() + "-" + nameChemical;

                if (massIntensityResults.ContainsKey(precursorAndMass))
                {
                    massIntensityResults[precursorAndMass].Add(intensity);
                }
                else
                {
                    List<double> intensityArray = new()
                    {
                        intensity
                    };
                    massIntensityResults.TryAdd(precursorAndMass, intensityArray);
                }
            }

            return massIntensityResults;
        }

        /// <summary>
        /// To collect the precursor and ionized ion information in DDMS2 data
        /// </summary>
        /// <param name="rawFile"></param>
        /// <returns></returns>
        public static ConcurrentDictionary<int, List<double[]>> CentroidCollectionDataMRM_v2(IRawDataExtended rawFile)
        {

            rawFile.SelectInstrument(Device.MS, 1);
            //all scans are used since it is a MSI data
            int[] scans = rawFile.GetFilteredScanEnumerator(rawFile.GetFilterFromString("")).ToArray(); // get all scans

            ConcurrentDictionary<int, List<double[]>> scanPrecursorMassIntensity =
                new ConcurrentDictionary<int, List<double[]>>();

            foreach (int scan in scans)
            {
                IScanEvent scanEvent = rawFile.GetScanEventForScanNumber(scan);

                if (scanEvent.MSOrder.ToString() == "Ms2")
                {
                    CentroidStream centroid = rawFile.GetCentroidStream(scan, false);
                    double[] mass3 = centroid.Masses;
                    double[] centroidValue = centroid.Intensities;
                    double precursor = Math.Round(scanEvent.GetReaction(0).PrecursorMass, 4);
                    double[] precursorArray = Enumerable.Repeat(precursor, mass3.Length).ToArray();

                    scanPrecursorMassIntensity.TryAdd(scan, new List<double[]> { precursorArray, mass3, centroidValue });
                }
            }
            return scanPrecursorMassIntensity;
        }

        /// <summary>
        /// To process the SIM .mzXML data and form the heatmap data
        /// </summary>
        /// <param name="spectraInfoList"></param>
        /// <param name="referenceScanNum"></param>
        /// <param name="intervalTime"></param>
        /// <param name="retentionTimeList"></param>
        /// <returns></returns>
        public static ConcurrentDictionary<double, ConcurrentDictionary<int, double[]>> CentroidCollectionDatamzXML(
            List<SpectrumInfo> spectraInfoList, int referenceScanNum, double intervalTime, double[] retentionTimeList, int rawFileNumber, double lower, double upper)
        {
            ConcurrentDictionary<double, ConcurrentDictionary<int, double[]>> mz_data = new();

            //Parallel.For(0, referenceScanNum, i =>
            for (int i = 0; i < referenceScanNum; i++)
            {
                double targetTime = intervalTime * (i + 1);

                List<double> minRetentionTime = new();
                minRetentionTime = retentionTimeList.Select(x => Math.Abs(x - targetTime)).ToList();
                int scanIndex = minRetentionTime.IndexOf(minRetentionTime.Min());

                IEnumerable<double> mzIntensity = spectraInfoList[scanIndex].IntensityList.Select(x => (double)x);
                List<double> mzList = spectraInfoList[scanIndex].MzList;

                if (lower != 0 & upper != 1000000.0)
                {
                    AFAMassIntensityPipeline.GatheringTheSpectrum(i, rawFileNumber, mzIntensity, mzList, mz_data, lower, upper, referenceScanNum);
                }
                else
                {
                    AFAMassIntensityPipeline.GatheringTheSpectrum(i, rawFileNumber, mzIntensity, mzList, mz_data, referenceScanNum);
                }
            }
            return mz_data;
        }

        /// <summary>
        /// To collect the precursor and ionized ion intensity info in AIF data
        /// </summary>
        /// <param name="rawFile"></param>
        /// <returns></returns>
        public static (ConcurrentDictionary<string, List<double>>, ConcurrentDictionary<string, List<double>>) CentroidCollectionDataMRM_v3
            (IRawDataExtended rawFile)
        {
            rawFile.SelectInstrument(Device.MS, 1);
            //all scans are used since it is a MSI data
            int[] scans = rawFile.GetFilteredScanEnumerator(rawFile.GetFilterFromString("")).ToArray(); // get all scans

            ConcurrentDictionary<string, List<double>> precursorMassIntensity =
                new ConcurrentDictionary<string, List<double>>();
            ConcurrentDictionary<string, List<double>> ionizedMassIntensity =
                new ConcurrentDictionary<string, List<double>>();

            if (scans.Length % 2 != 0)
            { scans = scans.Take(scans.Length - 1).ToArray(); }

            for (int scanIndex = 0; scanIndex < scans.Length; scanIndex++)
            //Parallel.For(0, scans.Length, scanIndex =>
            {
                int indexInEach;
                if (scanIndex % 2 != 0)
                {
                    indexInEach = (scanIndex - 1) / 2;
                }
                else
                {
                    indexInEach = scanIndex / 2;
                }

                IScanEvent scanEvent = rawFile.GetScanEventForScanNumber(scans[scanIndex]);
                double[] mass = rawFile.GetCentroidStream(scans[scanIndex], false).Masses;
                double[] centroidValue = rawFile.GetCentroidStream(scans[scanIndex], false).Intensities;

                for (int i = 0; i < mass.Length; i++)
                {
                    string mzKey = Math.Round(ProcessTheMz(mass[i]), 4).ToString();
                    if (scanEvent.MSOrder.ToString() == "Ms")
                    {
                        if (precursorMassIntensity.ContainsKey(mzKey))
                        {
                            precursorMassIntensity[mzKey][indexInEach] = centroidValue[i];
                        }
                        else
                        {
                            List<double> intensity_list = Enumerable.Repeat(0.0, scans.Length / 2).ToList();
                            intensity_list[indexInEach] = centroidValue[i]; // not thread safe !!!
                            precursorMassIntensity.TryAdd(mzKey, intensity_list);
                        }
                    }
                    else
                    {
                        if (ionizedMassIntensity.ContainsKey(mzKey))
                        {
                            ionizedMassIntensity[mzKey][indexInEach] = centroidValue[i];
                        }
                        else
                        {
                            List<double> intensity_list = Enumerable.Repeat(0.0, scans.Length / 2).ToList();
                            intensity_list[indexInEach] = centroidValue[i];
                            ionizedMassIntensity.TryAdd(mzKey, intensity_list);
                        }
                    }
                }
            }


            return (precursorMassIntensity, ionizedMassIntensity);
        }


        /// <summary>
        /// The method to match the mz
        /// </summary>
        /// <param name="MZ"></param>
        /// <returns></returns>
        public static double ProcessTheMz(double MZ)
        {
            double lower = 0.999995;
            double higher = 1.000005;

            if (MZ < 70 * higher)
            {
                double returnMZ = 70.0;
                return returnMZ;
            }
            else
            {
                double results = Math.Log(MZ / (70 * higher), higher / lower);
                double index = Math.Floor(results);
                double returnMZ = 70 * higher * Math.Pow(higher / lower, index);
                double returnMZ2 = 70 * higher * Math.Pow(higher / lower, index + 1);
                return (returnMZ + returnMZ2) / 2;
            }
        }

        public static double CalculateStandardDeviation(IEnumerable<int> values)
        {
            double standardDeviation = 0;

            if (values.Any())
            {
                // Compute the average.     
                double avg = values.Average();

                // Perform the Sum of (value-avg)_2_2.      
                double sum = values.Sum(d => Math.Pow(d - avg, 2));

                // Put it all together.      
                standardDeviation = Math.Sqrt((sum) / (values.Count() - 1));

                standardDeviation = Math.Round(standardDeviation, 2);
            }

            return standardDeviation;
        }

    }
}
