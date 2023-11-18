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
using System.Diagnostics;
using ThermoFisher.CommonCore.Data.Business;
using ThermoFisher.CommonCore.Data.Interfaces;
using Log = Serilog.Log;
using Microsoft.WindowsAPICodePack.Dialogs;
using RDotNet;
using MultiMSIProcessor.RIntegration;
using System.Drawing.Imaging;
using System.Text.RegularExpressions;
using MultiMSIProcessor.FunctionCollections;
using MSDataFileReader;
using static Microsoft.WindowsAPICodePack.Shell.PropertySystem.SystemProperties.System;
using ThermoFisher.CommonCore.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace MultiMSIProcessor
{
    public delegate void DataTransfer(string[] data);
    public partial class Form1 : Form
    {
        public DataTransfer transferDelegate;

        private RGraphAppHook cbt;
        public static REngine? engine;

        public Form1()
        {
            InitializeComponent();

            // need to run this exe first
            // "C:\Program Files\R\R-4.2.1\bin\x64\RSetReg.exe"
            REngine.SetEnvironmentVariables();
            engine = REngine.GetInstance();
            //https://github.com/rdotnet/rdotnet/issues/151 need to add the PATH to the Sys.getenv
            //the following three lines of code make sure the PATH was added to the system
            //textBox1Tab3.AppendText(engine.Evaluate("Sys.getenv('PATH')").AsCharacter()[0] + newLine + newLine);
            engine.Evaluate("Sys.setenv(PATH = paste(\"C:/Program Files/R/R-4.2.1/bin/x64\", Sys.getenv(\"PATH\"), sep=\";\"))");
            //textBox1Tab3.AppendText(engine.Evaluate("Sys.getenv('PATH')").AsCharacter()[0]);

            cbt = new RGraphAppHook
            {
                GraphControl = panel1Tab3ForBoxPlot
            };
        }

        //=============================================================================================================================================================================================
        //================================================================= Tab 1 =====================================================================================================================
        //=============================================================================================================================================================================================


        private string rootDirectoryLocation = null!;
        //the path to output the Tab 1 results
        private string exportLocation = null!;
        //readonly string currentDirectory;

        private ConcurrentDictionary<string, string[]> slicesAndRawFileNames = new();
        readonly ConcurrentDictionary<string, int> colNumDict = new();
        readonly ConcurrentDictionary<string, int> rawFileNumberDict = new();
        ConcurrentDictionary<string, ConcurrentDictionary<double, double[]>> mZAndMaxMinIntensityDict = new();// first one is the MAX

        // the raw file name, the mz, the rawFileNum, the colNum, the intensity
        ConcurrentDictionary<string, ConcurrentDictionary<double, ConcurrentDictionary<int, double[]>>> mZAndIntensityDict = new();
        ConcurrentDictionary<string, double> originalToPictureBoxRatioDict = new();
        readonly string newLine = Environment.NewLine;

        /// <summary>
        /// give the directory containing the slices data.
        /// the directroy containing files like NEG_Data_slice_1_sample_1
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Btn1SelectFolder_Click(object sender, EventArgs e)
        {
            slicesAndRawFileNames.Clear();
            using var fbd = new FolderBrowserDialog();
            DialogResult result = fbd.ShowDialog();

            if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
            {
                rootDirectoryLocation = fbd.SelectedPath;
                string[] files = Directory.GetDirectories(fbd.SelectedPath);

                if (files.Length == 0)
                {
                    textBox1Tab1.AppendText("There is no folder in the given directroy." + newLine);
                    return;
                }
                else
                {
                    AFAMassIntensityPipeline.GetTheFiles(files, slicesAndRawFileNames);
                }
            }
        }


        /// <summary>
        /// start extracting Raw data files from the chosen directory
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void Btn3Tab1StartExtracting_Click(object sender, EventArgs e)
        {

            if (slicesAndRawFileNames.IsEmpty)
            {
                MessageBox.Show("Please select a path with MSI folders within. Put .raw data in those MSI folders.");
            }
            else
            {
                //mZAndHeatbinDict.Clear();
                mZAndIntensityDict.Clear();
                mZAndMaxMinIntensityDict.Clear();
                listBox4Tab2MultipleSlices.Items.Clear();
                colNumDict.Clear();
                rawFileNumberDict.Clear();

                System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
                System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;

                Stopwatch allSlicesTime = new();
                allSlicesTime.Start();

                double upperLevel = 1000000.0;
                double lowerLevel = 0.0;
                if (textBoxTab1UpperLevel.Text != "")
                {
                    upperLevel = Convert.ToDouble(textBoxTab1UpperLevel.Text);
                }
                if (textBoxTab1LowerLevel.Text != "")
                {
                    lowerLevel = Convert.ToDouble(textBoxTab1LowerLevel.Text);
                }

                var myComparer = new CustomComparer();
                List<string> orderedKeys = slicesAndRawFileNames.Keys.ToList();
                orderedKeys.Sort(myComparer);

                for (int eachFileIndex = 0; eachFileIndex < orderedKeys.Count(); eachFileIndex++) // dive to each slice
                {
                    string key = orderedKeys[eachFileIndex];
                    string[] rawFiles = slicesAndRawFileNames[key];

                    ThreadHelperClass.SetText(this, textBox1Tab1, "Start extracting the " + key + " slice Data." + newLine);
                    ConcurrentDictionary<double, ConcurrentDictionary<int, double[]>> mZAndHeatbin = new();
                    ConcurrentDictionary<double, int> mZMissingTotal = new();

                    if (rawFiles == null)
                    {
                        ThreadHelperClass.SetText(this, textBox1Tab1, "No raw files were found in the " +
                            key + " slice Data." +
                            newLine + "Program stopped !" + newLine);
                        return;
                    }
                    else
                    {

                        Stopwatch singleFileTime = new();

                        #region Scan number calculation
                        singleFileTime.Start();
                        textBox1Tab1.AppendText("Recording the scan number in the " + key + " slice directory." + newLine);
                        List<int> scanNumberFluctuation = new();
                        await System.Threading.Tasks.Task.Run(() =>
                        {
                            scanNumberFluctuation = AFAMassIntensityPipeline.CalculateScanNumbers(rawFiles);
                        });
                        singleFileTime.Stop();
                        if (scanNumberFluctuation.Distinct().Count() > 1)
                        {
                            textBox1Tab1.AppendText("The scan number is fluctuating: mean(SD) " +
                                Math.Round(scanNumberFluctuation.Average(), 2) + "(" +
                                AFACentroidCollectionToList.CalculateStandardDeviation(scanNumberFluctuation) + "). Takes: " +
                                Math.Round(Convert.ToDouble(singleFileTime.ElapsedMilliseconds) / 1000.0, 2) + "s." + newLine);
                        }
                        else
                        {
                            if (scanNumberFluctuation.IsNullOrEmpty())
                            {
                                textBox1Tab1.AppendText("The MSI data format is .imzML." + newLine);
                            }
                            else
                            {
                                textBox1Tab1.AppendText("The scan number is all the same." + newLine);
                            }
                        }
                        singleFileTime.Reset();
                        #endregion

                        singleFileTime.Start();
                        if (upperLevel != 1000000.0)
                        {
                            textBox1Tab1.AppendText("Reading data with upper threshold:" + textBoxTab1UpperLevel.Text + "." + newLine);
                        }
                        else { textBox1Tab1.AppendText("Reading data with no upper threshold." + newLine); }
                        if (lowerLevel != 0)
                        {
                            textBox1Tab1.AppendText("Reading data with lower threshold:" + textBoxTab1LowerLevel.Text + "." + newLine);
                        }
                        else { textBox1Tab1.AppendText("Reading data with no lower threshold." + newLine); }

                        if (lowerLevel > upperLevel)
                        {
                            MessageBox.Show("Please adjust the threshold.");
                            return;
                        }
                        await System.Threading.Tasks.Task.Run(() =>
                        {
                            AFAMassIntensityPipeline.ReadInFromFiles(key, rawFiles, mZAndHeatbin,
                                colNumDict, rawFileNumberDict, originalToPictureBoxRatioDict, lowerLevel, upperLevel);
                        });
                        textBox1Tab1.AppendText("Finishing reading data of raw files from the " + key + " slice directory." + newLine);
                        textBox1Tab1.AppendText("Elapsed time: " + Math.Round(Convert.ToDouble(singleFileTime.ElapsedMilliseconds) / 1000.0, 2) + " s" + newLine);
                        textBox1Tab1.AppendText("Remaining mz number: " + mZAndHeatbin.Keys.Count() + newLine);
                        singleFileTime.Reset();

                        textBox1Tab1.AppendText("Starting to filtering m/z with 80% missing raw files" + newLine);
                        singleFileTime.Start();
                        mZAndHeatbin = AFAMassIntensityPipeline.ProcessTheMzInAllTheRaw_v2(rawFileNumberDict[key], colNumDict[key], mZAndHeatbin);
                        singleFileTime.Stop();
                        textBox1Tab1.AppendText("Elapsed time: " + Math.Round(Convert.ToDouble(singleFileTime.ElapsedMilliseconds) / 1000.0, 2) + " s" + newLine);

                        textBox1Tab1.AppendText("Remaining m/z number: " + mZAndHeatbin.Keys.Count() + newLine);

                        singleFileTime.Reset();

                        mZAndIntensityDict.TryAdd(key, mZAndHeatbin);
                    }
                }

                await System.Threading.Tasks.Task.Run(() =>
                {
                    Parallel.ForEach(mZAndIntensityDict, i =>
                    {
                        ConcurrentDictionary<double, double[]> mZAndMaxMinIntensity = new();

                        mZAndMaxMinIntensity = AFAMassIntensityPipeline.ExtractMaxMinIntensity_v2(i.Value);
                        mZAndMaxMinIntensityDict.TryAdd(i.Key, mZAndMaxMinIntensity);
                    });
                });

                Log.CloseAndFlush();

                allSlicesTime.Stop();
                textBox1Tab1.AppendText("Time to process all " + slicesAndRawFileNames.Count + " slices: " +
                    Math.Round(Convert.ToDouble(allSlicesTime.ElapsedMilliseconds) / 1000.0, 2) + "s" +
                    newLine);
                textBox1Tab1.AppendText("Please switch to Tab 2: Tissue selection for further analyzing" + newLine);

                listBox1Tab2MZList.Items.Clear(); // mz listbox
                listBox4Tab2MultipleSlices.Items.Clear();

                for (int i = 0; i < orderedKeys.Count(); i++)
                {
                    listBox4Tab2MultipleSlices.Items.Add(orderedKeys.ElementAt(i));
                }
            }

        }

        /// <summary>
        /// export All the data after extracting w.o further analysing
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void Btn2Tab1Export_Click(object sender, EventArgs e)
        {
            CommonOpenFileDialog dialog = new()
            {
                InitialDirectory = @"D:\",
                IsFolderPicker = true
            };
            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                exportLocation = dialog.FileName;
                string writeCol = "mz_Value";

                await System.Threading.Tasks.Task.Run(() =>
                {
                    Parallel.ForEach(mZAndIntensityDict.Keys, index =>
                    {
                        ThreadHelperClass.SetText(this, textBox1Tab1, "Start to export for slice: " + index + "." + newLine);
                        Stopwatch singleMSITime = new();
                        singleMSITime.Start();
                        int colMin = colNumDict[index];

                        for (int i = 0; i < colMin; i++)
                        {
                            writeCol = writeCol + "\t" + "intensity" + (i + 1);
                        }

                        string[] eachSlicesDataDirectory = Directory.GetDirectories(exportLocation, "*", SearchOption.TopDirectoryOnly);
                        string eachSlicesDataDirectoryName = exportLocation + @"\" + index + "_txt_export";

                        if (!eachSlicesDataDirectory.Contains(eachSlicesDataDirectoryName))
                        {
                            _ = Directory.CreateDirectory(eachSlicesDataDirectoryName);
                        }
                        else
                        {
                            DirectoryInfo di = new(eachSlicesDataDirectoryName);
                            foreach (FileInfo file in di.EnumerateFiles())
                            {
                                file.Delete();
                            }
                        }
                        AFAMassIntensityPipeline.WriteAFAData_v2(mZAndIntensityDict[index], eachSlicesDataDirectoryName + @"\", writeCol, rawFileNumberDict[index], colMin);
                        //textBox1Tab1.AppendText("Complete export filtered results for slice: " + index + newLine);
                        singleMSITime.Stop();
                        ThreadHelperClass.SetText(this, textBox1Tab1, "Complete export filtered results for slice: " + index + ". Time: " +
                            Math.Round(Convert.ToDouble(singleMSITime.ElapsedMilliseconds / 1000), 2) + "s." +
                            newLine);
                    });
                });
            }
        }



        //==========================================================================================================================================================================================================
        //=========================================================== Tab 2 ========================================================================================================================================
        //==========================================================================================================================================================================================================


        /// <summary>
        /// to load/input the m/z data (txt files generated from tab 1) in the selected folder to the program
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void BtnTab2ChooseDirecClick(object sender, EventArgs e)
        {
            //mZAndHeatbinDict.Clear();
            mZAndIntensityDict.Clear();
            mZAndMaxMinIntensityDict.Clear();
            slicesAndRawFileNames.Clear();
            listBox4Tab2MultipleSlices.Items.Clear();
            colNumDict.Clear();
            rawFileNumberDict.Clear();

            using (var fbd = new FolderBrowserDialog())
            {
                DialogResult expor = fbd.ShowDialog();

                if (expor == DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
                {
                    rootDirectoryLocation = fbd.SelectedPath;
                    textBox1Tab2.AppendText("Input directory was confirmed: " + rootDirectoryLocation + newLine);
                    textBox1Tab2.AppendText("The read in from txt process begin." + newLine);
                    string[] files = Directory.GetDirectories(fbd.SelectedPath);

                    if (files.Length != 0)
                    {
                        Stopwatch slideReadIntxtTime = new();
                        Stopwatch alltxtReadinTime = new();
                        alltxtReadinTime.Start();

                        foreach (string slicesFileName in files) // for each slice
                        {
                            slideReadIntxtTime.Start();
                            ConcurrentDictionary<double, double[]> mZAndMaxMinIntensity = new();
                            ConcurrentDictionary<double, ConcurrentDictionary<int, double[]>> mZAndHeatbin = new();
                            ConcurrentDictionary<double, Bitmap> mZAndHeatmap = new();

                            string keySliceNames = slicesFileName.Substring(slicesFileName.LastIndexOf(@"\") + 1);

                            string[] txtFilesInSliceDirectroy = Directory.GetFiles(slicesFileName, "*.txt", SearchOption.TopDirectoryOnly);
                            string[] txtFilesShortNameInSliceDirectroy = new string[txtFilesInSliceDirectroy.Count()];

                            List<int> rawFileNumber = new();
                            List<int> colNumList = new();
                            await System.Threading.Tasks.Task.Run(() =>
                            {
                                for (int j = 0; j < txtFilesInSliceDirectroy.Count(); j++)// for each txt file
                                {
                                    string rawFileofd = txtFilesInSliceDirectroy[j];
                                    rawFileofd = rawFileofd.Substring(rawFileofd.LastIndexOf(@"\") + 1);

                                    if (!double.TryParse(rawFileofd.Substring(0, rawFileofd.Length - 4), out double key))
                                    {
                                        MessageBox.Show("the " + j + "file in the " + keySliceNames + " folder is not a file named with m/z");
                                    }

                                    txtFilesShortNameInSliceDirectroy[j] = key.ToString();

                                    //lineNum equals to the raw file number
                                    int lineNumTotal = File.ReadLines(txtFilesInSliceDirectroy[j]).Count() - 1;
                                    //colNum equals to the scan number
                                    int colNum = 0;

                                    ConcurrentDictionary<int, double[]> heatbin2 = new();

                                    using (var reader = File.OpenText(txtFilesInSliceDirectroy[j]))
                                    {
                                        string headerLine = reader.ReadLine();
                                        int lineNum = 0;

                                        while (!reader.EndOfStream)
                                        {
                                            double[] eachLine = reader.ReadLine().Split((char)9).Select(m => double.Parse(m)).ToArray();
                                            colNum = eachLine.Count();
                                            heatbin2.TryAdd(lineNum, eachLine.Where((item, index) => index != 0).ToArray());
                                            lineNum++;
                                        }
                                    }
                                    mZAndHeatbin.TryAdd(key, heatbin2);
                                    rawFileNumber.Add(lineNumTotal);
                                    colNumList.Add(colNum - 1);
                                }
                            });
                            // check the read in data
                            if (rawFileNumber.Distinct().Count() != 1)
                            {
                                textBox1Tab2.AppendText("The input files do not fit due to the varying rows (the raw file number)" + newLine);
                                return;
                            }

                            if (colNumList.Distinct().Count() != 1)
                            {
                                textBox1Tab2.AppendText("The input files do not fit to the varying columns (the scan number)");
                                return;
                            }

                            colNumDict.TryAdd(keySliceNames, colNumList[0]);
                            rawFileNumberDict.TryAdd(keySliceNames, rawFileNumber[0]);
                            originalToPictureBoxRatioDict.TryAdd(keySliceNames, 1.8);

                            slideReadIntxtTime.Stop();
                            textBox1Tab2.AppendText("Time to read in slice " + keySliceNames + " take " + Math.Round(Convert.ToDouble(slideReadIntxtTime.ElapsedMilliseconds) / 1000.0, 2) + "s." + newLine);
                            slideReadIntxtTime.Reset();
                            // process and create
                            mZAndMaxMinIntensity = AFAMassIntensityPipeline.ExtractMaxMinIntensity_v2(mZAndHeatbin);

                            // Add the corresponding data
                            mZAndIntensityDict.TryAdd(keySliceNames, mZAndHeatbin);
                            mZAndMaxMinIntensityDict.TryAdd(keySliceNames, mZAndMaxMinIntensity);

                            listBox4Tab2MultipleSlices.Items.Add(keySliceNames);
                            slicesAndRawFileNames.TryAdd(keySliceNames, txtFilesShortNameInSliceDirectroy);
                            // reminder: if use this method to read in data, then slicesAndRawFileNames.values are short of PATH prefix !!!
                        }
                        alltxtReadinTime.Stop();
                        textBox1Tab2.AppendText("Time to read in all slice folders take " + Math.Round(Convert.ToDouble(alltxtReadinTime.ElapsedMilliseconds) / 1000.0, 2) + "s." + newLine);
                    }
                    else
                    {
                        textBox1Tab2.AppendText("No folders found." + newLine);
                        return;
                    }
                }
            }
        }


        private void listBox4Tab2MultipleSlices_SelectedIndexChanged(object sender, EventArgs e)
        {
            listBox1Tab2MZList.Items.Clear();
            if (listBox4Tab2MultipleSlices.SelectedItem != null)
            {
                // get the mz list in the slices and display
                List<string> mzList = mZAndIntensityDict[listBox4Tab2MultipleSlices.SelectedItem.ToString()].Keys.
                    ToList().OrderBy(k => k).Select(k => k.ToString()).ToList();

                foreach (string mz in mzList)
                {
                    listBox1Tab2MZList.Items.Add(mz);
                }

                string selectedSlice = listBox4Tab2MultipleSlices.SelectedItem.ToString();
                label25.Text = colNumDict[selectedSlice].ToString() + " pixels";
                label26.Text = rawFileNumberDict[selectedSlice].ToString() + " pixels";
            }
        }

        //stretchRatio Dictionary
        //the stretch ratio: picturebox/actual size
        ConcurrentDictionary<string, double> stretchRatioXYDict = new();

        private void button2Tab2ClearFiltering_Click(object sender, EventArgs e)
        {
            rectFilterWithinDict.Clear();
            rectFilterOutsideDict.Clear();
            pictureBoxTab2.Invalidate();

            listBox2Tab2Group.Items.Clear();
            listBox3Tab2ROI.Items.Clear();

            rectGroupSliceDataDict.Clear();
            exportROINumEachGroup.Clear();
            sliceAndROIRectangleIndex.Clear();
            sliceAndROIRectangleIndex.Clear();
            ROIRectListDict.Clear();
        }


        ConcurrentDictionary<string, bool> widerOrHigherDict = new();

        /// <summary>
        /// show the heatmaps of the corresponding mz in the listBox1Tab2
        /// stretchRatioXYDict means how the picture was stretched to meet either the height or the width of the picture box.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void listBox1Tab2MzList_SelectedIndexChanged(object sender, EventArgs e)
        {
            double pictureBoxXYRatio = (double)pictureBoxTab2.Width / pictureBoxTab2.Height;
            // add the if when click is outside the box
            if (listBox1Tab2MZList.SelectedItems.Count != 0)
            {
                if (listBox4Tab2MultipleSlices.SelectedItems.Count > 0)
                {
                    string sliceKey = listBox4Tab2MultipleSlices.SelectedItem.ToString();
                    labelTab2ShowTheMaxIntensity.Text = Math.Round(
                        mZAndMaxMinIntensityDict[sliceKey][double.Parse(listBox1Tab2MZList.SelectedItem.ToString())][0], 0).ToString();

                    Bitmap chosenPicture = AFAMassIntensityPipeline.SingleMzAndHeatmap_v2(
                        mZAndIntensityDict[sliceKey][double.Parse(listBox1Tab2MZList.SelectedItem.ToString())],
                        rawFileNumberDict[sliceKey], colNumDict[sliceKey],
                        mZAndMaxMinIntensityDict[sliceKey][double.Parse(listBox1Tab2MZList.SelectedItem.ToString())][0],
                        originalToPictureBoxRatioDict[sliceKey]);

                    bool widerOrHigher = (chosenPicture.Width / chosenPicture.Height) > pictureBoxXYRatio;

                    widerOrHigherDict.AddOrUpdate(sliceKey, widerOrHigher, (key, oldVal) => widerOrHigher);

                    pictureBoxTab2.Image = chosenPicture;
                    pictureBoxTab2.SizeMode = PictureBoxSizeMode.Zoom;

                    if (widerOrHigher)
                    {
                        stretchRatioXYDict.AddOrUpdate(sliceKey, (double)chosenPicture.Width / pictureBoxTab2.Size.Width, (key, oldVal) => (double)chosenPicture.Width / pictureBoxTab2.Size.Width);
                    }
                    else
                    {
                        stretchRatioXYDict.AddOrUpdate(sliceKey, (double)chosenPicture.Height / pictureBoxTab2.Size.Height, (key, oldVal) => (double)chosenPicture.Height / pictureBoxTab2.Size.Height);
                    }
                }
            }
            else
            {
                return;
            }
        }

        /// <summary>
        /// find the mz in the mz List
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void textBox2Tab2Searchmz_TextChanged(object sender, EventArgs e)
        {
            var textBox = (System.Windows.Forms.TextBox)sender;
            listBox1Tab2MZList.SelectedIndex = textBox.TextLength == 0 ?
                -1 : listBox1Tab2MZList.FindString(textBox.Text);
        }
        /// <summary>
        /// change the picture shown in the picturebox as the Key up and down changing
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void textBox2Tab2Searchmz_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Up | e.KeyCode == Keys.Down)
            {
                listBox1Tab2MZList.Focus();
                listBox1Tab2MZList.Select();

                if (e.KeyCode == Keys.Up)
                {
                    listBox1Tab2MZList.SelectedIndex--;
                }

                if (e.KeyCode == Keys.Down)
                {
                    listBox1Tab2MZList.SelectedIndex++;
                }
            }
        }


        //private readonly List<Rectangle> rectFilterWithin = new();
        private readonly ConcurrentDictionary<string, List<Rectangle>> rectFilterWithinDict = new();
        //private readonly List<Rectangle> rectFilterOutside = new();
        private readonly ConcurrentDictionary<string, List<Rectangle>> rectFilterOutsideDict = new();


        private Rectangle Rect = new();
        private Point RectStartPoint;

        // key is slice name and value is the added ROI rectangles
        private readonly ConcurrentDictionary<string, List<Rectangle>> ROIRectListDict = new();

        private Pen _Pen = new(Color.Black, 4);
        private bool LeftMousePressed = false;

        /// <summary>
        /// make the selection through click to
        /// 1. distinguish the tissue area using within or outside;
        ///    ps. left click to choose and right click to withdraw.
        /// 2. create the ROI area, which was set to be random/small/medium/large rectangle;
        ///    ps. the small ; medium ; large ; 
        /// 3. originalToPictureBoxRatio is the actual length of (height / width) of the MSI
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PictureBoxTab2_MouseDown(object sender, MouseEventArgs e)
        {
            if (listBox4Tab2MultipleSlices.SelectedItems.Count > 0)
            {
                if (listBox1Tab2MZList.SelectedItems.Count > 0)
                {
                    string sliceName = listBox4Tab2MultipleSlices.SelectedItem.ToString();
                    Rect.Location = e.Location;

                    double stretchRatio = stretchRatioXYDict[sliceName];
                    //int pictureHeight = (int)(rawFileNumberDict[sliceName] * originalToPictureBoxRatioDict[sliceName] / stretchRatio);
                    //int pictureWidth = (int)(colNumDict[sliceName] / stretchRatio);
                    int pictureHeight = pictureBoxTab2.Image.Height;
                    int pictureWidth = pictureBoxTab2.Image.Width;
                    bool widerOrHigher = widerOrHigherDict[sliceName];

                    if (!checkBox1Tab2SelectROI.Checked)
                    {
                        if (e.Button == MouseButtons.Left) // if left mouse click
                        {
                            Rect.Size = new Size(pictureBoxTab2.Width / 16, pictureBoxTab2.Height / 16);

                            Rect = AFAMassIntensityPipeline.RepositionTheRects(widerOrHigher, Rect, pictureBoxTab2, pictureHeight, pictureWidth, stretchRatio);

                            if (radioButton1Tab2Within.Checked == true)
                            {
                                rectFilterWithinDict.AddOrUpdate(sliceName, new List<Rectangle> { Rect },
                                    (key, oldVal) => oldVal.Append(Rect).ToList());
                            }
                            if (radioButton2Tab2Outside.Checked == true)
                            {
                                rectFilterOutsideDict.AddOrUpdate(sliceName, new List<Rectangle> { Rect },
                                    (key, oldVal) => oldVal.Append(Rect).ToList());
                            }
                        }
                        else // if right mouse click
                        {
                            if (radioButton1Tab2Within.Checked == true)
                            {
                                if (rectFilterWithinDict[sliceName].Count > 0)
                                {
                                    rectFilterWithinDict[sliceName].RemoveAt(rectFilterWithinDict[sliceName].Count - 1);
                                }
                            }
                            if (radioButton2Tab2Outside.Checked == true)
                            {
                                if (rectFilterOutsideDict[sliceName].Count > 0)
                                {
                                    rectFilterOutsideDict[sliceName].RemoveAt(rectFilterOutsideDict[sliceName].Count - 1);
                                }
                            }
                            pictureBoxTab2.Invalidate();
                        }
                    }
                    else // if ROI choosing mode
                    {
                        if (radioButton1Tab2Within.Checked == true | radioButton2Tab2Outside.Checked == true)
                        {
                            MessageBox.Show("Please uncheck the ROI selection.");
                            return;
                        }
                        else
                        {
                            if (e.Button == MouseButtons.Left)
                            {
                                if (radioButton3Tab2Dragging.Checked == true)
                                {
                                    RectStartPoint = e.Location;
                                }
                                else
                                {
                                    if (radioButton4Tab2Small.Checked == true)
                                    {
                                        Rect.Size = new Size((int)Math.Ceiling(0.1 * (pictureWidth / stretchRatio)), (int)Math.Ceiling(0.1 * (pictureHeight / stretchRatio)));
                                    }

                                    if (radioButton5Tab2Medium.Checked == true)
                                    {
                                        Rect.Size = new Size((int)Math.Ceiling(0.2 * (pictureWidth / stretchRatio)), (int)Math.Ceiling(0.2 * (pictureHeight / stretchRatio)));
                                    }

                                    if (radioButton6Tab2Large.Checked == true)
                                    {
                                        Rect.Size = new Size((int)Math.Ceiling(0.3 * (pictureWidth / stretchRatio)), (int)Math.Ceiling(0.3 * (pictureHeight / stretchRatio)));
                                    }
                                    Rect = AFAMassIntensityPipeline.RepositionTheRects(widerOrHigher, Rect, pictureBoxTab2, pictureHeight, pictureWidth, stretchRatio);
                                }
                            }
                        }
                    }
                }
                else
                {
                    MessageBox.Show("Please select the m/z first.");
                    return;
                }
            }
            else
            {
                MessageBox.Show("Please select the MSI file first.");
                return;
            }
        }

        /// <summary>
        /// create a rectangle with random size through dragging the mouse
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PictureBoxTab2_MouseMove(object sender, MouseEventArgs e)
        {
            if (listBox4Tab2MultipleSlices.SelectedItems.Count > 0)
            {
                if (listBox1Tab2MZList.SelectedItems.Count > 0)
                {
                    string sliceName = listBox4Tab2MultipleSlices.SelectedItem.ToString();
                    double stretchRatio = stretchRatioXYDict[sliceName];
                    int pictureHeight = pictureBoxTab2.Image.Height;
                    int pictureWidth = pictureBoxTab2.Image.Width;

                    bool widerOrHigher = widerOrHigherDict[sliceName];

                    if (checkBox1Tab2SelectROI.Checked)
                    {
                        if (radioButton3Tab2Dragging.Checked == true)
                        {
                            if (e.Button != MouseButtons.Left)
                            {
                                pictureBoxTab2.Invalidate();
                                return;
                            }
                            else
                            {
                                LeftMousePressed = true;
                                Point tempEndPoint = e.Location;

                                Rect.Location = new Point(
                                    Math.Min(RectStartPoint.X, tempEndPoint.X),
                                    Math.Min(RectStartPoint.Y, tempEndPoint.Y));

                                Rect.Size = new Size(
                                    Math.Abs(RectStartPoint.X - tempEndPoint.X),
                                    Math.Abs(RectStartPoint.Y - tempEndPoint.Y));

                                Rect = AFAMassIntensityPipeline.RepositionTheRects(widerOrHigher, Rect, pictureBoxTab2, pictureHeight, pictureWidth, stretchRatio);
                            }
                        }
                    }
                    pictureBoxTab2.Invalidate();
                }
            }
        }

        private ConcurrentDictionary<string, int> sliceAndROIRectangleIndex = new();

        /// <summary>
        /// When creating the ROI, a rectangle will be created everytime mouse up.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PictureBoxTab2_MouseUp(object sender, MouseEventArgs e)
        {
            if (listBox4Tab2MultipleSlices.SelectedItems.Count > 0)
            {
                string sliceName = listBox4Tab2MultipleSlices.SelectedItem.ToString();

                if (listBox1Tab2MZList.SelectedItems.Count > 0)
                {
                    if (checkBox1Tab2SelectROI.Checked == true)
                    {
                        if (Rect.Width > 0 & Rect.Height > 0)
                        {
                            sliceAndROIRectangleIndex.AddOrUpdate(sliceName, 1, (key, oldVal) => oldVal + 1);
                            listBox3Tab2ROI.Items.Add("Rectangle " + sliceAndROIRectangleIndex[sliceName] + " in the Slice: " + sliceName);
                            ROIRectListDict.AddOrUpdate(sliceName, new List<Rectangle> { Rect }, (key, oldVal) => oldVal.Append(Rect).ToList());
                        }
                    }
                    pictureBoxTab2.Invalidate();
                }
            }
            LeftMousePressed = false;
        }

        /// <summary>
        /// Set the heatmap upper threshold in textBoxTab2Threshold using Enter Key.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void textBoxTab2Threshold_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (textBoxTab2Threshold.Text.Length > 0)
                {
                    int threshold;
                    if (int.TryParse(textBoxTab2Threshold.Text, out threshold))
                    {
                        if (pictureBoxTab2.Image != null | listBox1Tab2MZList.SelectedItems.Count == 0)
                        {
                            string sliceName = listBox4Tab2MultipleSlices.SelectedItem.ToString();
                            double mz = double.Parse(listBox1Tab2MZList.SelectedItem.ToString());
                            double max = mZAndMaxMinIntensityDict[sliceName][mz][0];

                            Bitmap newHeatmap = new(colNumDict[sliceName], rawFileNumberDict[sliceName], PixelFormat.Format32bppArgb);

                            ConcurrentDictionary<int, double[]> each = mZAndIntensityDict[sliceName][mz];

                            //if (max >= threshold)
                            //{
                            for (int i = 0; i < rawFileNumberDict[sliceName]; i++)
                            {
                                if (each.ContainsKey(i))
                                {
                                    for (int j = 0; j < colNumDict[sliceName]; j++)
                                    {
                                        double intensityVal = each[i][j];
                                        if (intensityVal > threshold) { newHeatmap.SetPixel(j, i, Color.FromArgb(255, 255, 0, 0)); }
                                        else
                                        {
                                            newHeatmap.SetPixel(j, i, AFAMassIntensityPipeline.BasicColorMapping(intensityVal / max));
                                        }
                                    }
                                }
                                else
                                {
                                    for (int j = 0; j < colNumDict[sliceName]; j++)
                                    {
                                        newHeatmap.SetPixel(j, i, Color.FromArgb(255, 255, 0, 0));
                                    }
                                }
                            }
                            Bitmap outputResized = new(newHeatmap, new Size(newHeatmap.Width, (int)Math.Ceiling(originalToPictureBoxRatioDict[sliceName] * newHeatmap.Height)));
                            pictureBoxTab2.Image = outputResized;
                        }
                        else
                        {
                            MessageBox.Show("Please choose a picture.");
                            return;
                        }
                    }
                    else
                    {
                        MessageBox.Show("Please enter a number in a correct form, e.g. 1000.");
                        return;
                    }
                }
                else
                {
                    MessageBox.Show("Please enter a number.");
                    return;
                }
            }
        }


        private void buttonTab2ExportTheImage_Click(object sender, EventArgs e)
        {
            if (pictureBoxTab2.Image != null)
            {
                CommonOpenFileDialog dialog = new()
                {
                    IsFolderPicker = true
                };

                string pngExportLocation;
                if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
                {
                    pngExportLocation = dialog.FileName + @"\";
                    string fileNameHeatMap = pngExportLocation + listBox1Tab2MZList.SelectedItem.ToString() + ".png";
                    using (FileStream f = File.Open(fileNameHeatMap, FileMode.Create))
                    {
                        pictureBoxTab2.Image.Save(f, ImageFormat.Png);
                        textBox1Tab2.AppendText("Finishing exporting png file." + newLine);
                    }
                }
                else
                {
                    return;
                }
            }
            else
            {
                MessageBox.Show("Please select the image first");
                return;
            }
        }

        /// <summary>
        /// After picking a folder, export all the images for a certain m/z.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ButtonTab2ExportAllImageForAmz_Click(object sender, EventArgs e)
        {
            if (pictureBoxTab2.Image != null)
            {
                CommonOpenFileDialog dialog = new()
                {
                    IsFolderPicker = true
                };

                string pngExportLocation;
                if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
                {
                    pngExportLocation = dialog.FileName + @"\";
                }
                else
                {
                    return;
                }

                int count = 0;
                for (int x = 0; x < mZAndIntensityDict.Count; x++)
                {
                    if (listBox1Tab2MZList.SelectedItem == null)
                    {
                        MessageBox.Show("Please select the m/z first");
                        return;
                    }
                    else
                    {
                        if (mZAndIntensityDict.ElementAt(x).Value.Keys.Contains(double.Parse(listBox1Tab2MZList.SelectedItem.ToString())))
                        {
                            count++;

                            string sliceName = mZAndIntensityDict.Keys.ElementAt(x).ToString();
                            double mz = double.Parse(listBox1Tab2MZList.SelectedItem.ToString());
                            double max = mZAndMaxMinIntensityDict[sliceName][mz][0];
                            string fileNameHeatMap = pngExportLocation + mz + "_in_" + mZAndIntensityDict.Keys.ElementAt(x).ToString() + ".png";

                            ConcurrentDictionary<int, double[]> each = mZAndIntensityDict[sliceName][mz];

                            if (textBoxTab2Threshold.Text.Length > 0)
                            {
                                int threshold;
                                if (int.TryParse(textBoxTab2Threshold.Text, out threshold))
                                {
                                    Bitmap newHeatmap = new(colNumDict[sliceName], rawFileNumberDict[sliceName], PixelFormat.Format32bppArgb);

                                    for (int i = 0; i < rawFileNumberDict[sliceName]; i++)
                                    {
                                        if (each.ContainsKey(i))
                                        {
                                            for (int j = 0; j < colNumDict[sliceName]; j++)
                                            {
                                                double intensityVal = each[i][j];
                                                if (intensityVal > threshold) { newHeatmap.SetPixel(j, i, Color.FromArgb(255, 255, 0, 0)); }
                                                else
                                                {
                                                    newHeatmap.SetPixel(j, i, AFAMassIntensityPipeline.BasicColorMapping(intensityVal / max));
                                                }
                                            }
                                        }
                                        else
                                        {
                                            for (int j = 0; j < colNumDict[sliceName]; j++)
                                            {
                                                newHeatmap.SetPixel(j, i, Color.FromArgb(255, 255, 0, 0));
                                            }
                                        }
                                    }
                                    // the output x-y ratio should be adjusted in the future
                                    Bitmap outputResized = new(newHeatmap, new Size(newHeatmap.Width, (int)Math.Ceiling(1.8 * newHeatmap.Height)));

                                    using (FileStream f = File.Open(fileNameHeatMap, FileMode.Create))
                                    {
                                        outputResized.Save(f, ImageFormat.Png);
                                    }
                                }
                                else
                                {
                                    MessageBox.Show("Please enter a number in a correct form, e.g. 1000.");
                                    return;
                                }
                            }
                            else
                            {
                                using (FileStream f = File.Open(fileNameHeatMap, FileMode.Create))
                                {
                                    Bitmap heatmapToSave = AFAMassIntensityPipeline.SingleMzAndHeatmap_v2(mZAndIntensityDict[sliceName][mz],
                            rawFileNumberDict[sliceName], colNumDict[sliceName], mZAndMaxMinIntensityDict[sliceName][mz][0],
                            originalToPictureBoxRatioDict[sliceName]);
                                    heatmapToSave.Save(f, ImageFormat.Png);
                                }
                            }
                        }
                    }
                }
                textBox1Tab2.AppendText(count + " MSI experiments contain m/z: " + listBox1Tab2MZList.SelectedItem.ToString() + " and the images were exported" + newLine);
            }
            else
            {
                MessageBox.Show("Please select the m/z first");
                return;
            }
        }

        /// <summary>
        /// Set the group number in textBox1Tab2GroupNum using Enter Key.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void textBox1Tab2GroupNum_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                listBox2Tab2Group.Items.Clear();
                if (int.TryParse(textBox1Tab2GroupNum.Text, out int groupNum))
                {
                    if (groupNum == 0)
                    {
                        MessageBox.Show("please provide more than one group for the following analysis");
                    }
                    else
                    {
                        for (int i = 1; i <= groupNum; i++)
                        {
                            listBox2Tab2Group.Items.Add("Group" + i);
                        }
                    }
                }
            }
        }

        public Tuple<string, List<Rectangle>> highlightedRecListDict;

        /// <summary>
        /// Highlighted the selected Itmes in listBox3Tab2ROI within the listBox4Tab2MultipleSlices selected slice
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void listBox3Tab2ROI_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listBox4Tab2MultipleSlices.SelectedItems.Count > 0)
            {
                string listBox4SliceName = listBox4Tab2MultipleSlices.SelectedItem.ToString();

                List<Rectangle> highlightedRecList = new();
                if (listBox3Tab2ROI.SelectedItems.Count > 0)
                {
                    foreach (string selectedROI in listBox3Tab2ROI.SelectedItems)
                    {
                        int markerIndex = selectedROI.IndexOf("in the Slice: ");
                        string listBox3Tab2SliceName = selectedROI.Substring(markerIndex + 14); // "in the Slice: " is 14
                                                                                                //20221127 add tryparse
                        int ROIRectIndex = int.Parse(selectedROI.Substring(0, markerIndex).Substring(selectedROI.IndexOf("Rectangle ") + 10)); // "Rectangle " is 10
                        if (listBox3Tab2SliceName == listBox4SliceName)
                        {
                            highlightedRecList.Add(ROIRectListDict[listBox4SliceName][ROIRectIndex - 1]);
                        }
                    }
                    highlightedRecListDict = Tuple.Create(listBox4SliceName, highlightedRecList);
                    pictureBoxTab2.Invalidate();
                }
                else
                {
                    pictureBoxTab2.Invalidate();
                }
            }
            else
            {
                MessageBox.Show("Please select slice.");
                return;
            }
        }


        //first key is the group and the second is the slice name, the value is the one-based index of ROI and the corresponding rectangle
        static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, List<(int, Rectangle)>>> rectGroupSliceDataDict = new();
        //key is the group name and the value is the number of ROI
        ConcurrentDictionary<string, int> exportROINumEachGroup = new ConcurrentDictionary<string, int>();

        /// <summary>
        /// Add the selected ROI in listBox3Tab2ROI to the Group
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void button3Tab2AddROIToGroup_Click(object sender, EventArgs e)
        {
            if (listBox2Tab2Group.SelectedItems.Count > 0)
            {
                if (listBox3Tab2ROI.SelectedItems.Count > 0)
                {
                    string groupName = listBox2Tab2Group.SelectedItem.ToString();

                    if (rectGroupSliceDataDict.Keys.Contains(groupName))
                    {
                        rectGroupSliceDataDict[groupName].Clear();
                    }

                    exportROINumEachGroup.AddOrUpdate(groupName, listBox3Tab2ROI.SelectedItems.Count, (key, value) => listBox3Tab2ROI.SelectedItems.Count);

                    foreach (object selectedItem in listBox3Tab2ROI.SelectedItems)
                    {
                        string selectedItemsNames = selectedItem.ToString();
                        int markerIndex = selectedItemsNames.IndexOf("in the Slice: ");
                        string sliceName = selectedItemsNames.Substring(markerIndex + 14); // "in the Slice: " is 14
                                                                                           //20221127 add tryparse
                                                                                           //20221130 in fact, this argument should be written in another way
                        int ROIIndex = int.Parse(selectedItemsNames.Substring(0, markerIndex).Substring(selectedItemsNames.IndexOf("Rectangle ") + 10)); // "Rectangle " is 10

                        Rectangle selectedROIRectangles = ROIRectListDict[sliceName][ROIIndex - 1];

                        if (rectGroupSliceDataDict.Keys.Contains(groupName))
                        {
                            rectGroupSliceDataDict[groupName].AddOrUpdate(sliceName,
                                new List<(int, Rectangle)> { (ROIIndex, selectedROIRectangles) },
                                (key, oldVal) => oldVal.Append((ROIIndex, selectedROIRectangles)).ToList());
                        }
                        else
                        {
                            ConcurrentDictionary<string, List<(int, Rectangle)>> recListToAdd = new();
                            recListToAdd.TryAdd(sliceName, new List<(int, Rectangle)> { (ROIIndex, selectedROIRectangles) });
                            rectGroupSliceDataDict.TryAdd(groupName, recListToAdd);
                        }
                    }

                    listBox2Tab2Group.ClearSelected();
                    listBox3Tab2ROI.ClearSelected();
                    pictureBoxTab2.Invalidate();
                }
                else
                {
                    MessageBox.Show("Please select the ROI to add.");
                    return;
                }
            }
            else
            {
                MessageBox.Show("Please select the Group to add ROI.");
                return;
            }
        }

        /// <summary>
        /// Paint in the PictureBoxTab2 for the following items
        /// 1. the within and outside rectangles with selectionBrushWithin and selectionBrushOutside
        /// 2. the ROI rectangles (w.i. or w.0. highlighted)
        /// 3. change the color after ROI was selected or added to a group
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PictureBoxTab2_Paint(object sender, PaintEventArgs e)
        {
            Brush selectionBrushWithin = new SolidBrush(Color.FromArgb(128, 227, 255, 82));
            Brush selectionBrushOutside = new SolidBrush(Color.FromArgb(128, 239, 105, 154));

            Brush selectedROIColor = new SolidBrush(Color.FromArgb(128, 72, 145, 220));
            Brush selectionBrushHighlight = new SolidBrush(Color.FromArgb(200, 36, 146, 255));
            Brush selectionBrushGroup = new SolidBrush(Color.FromArgb(128, 184, 33, 189));

            if (pictureBoxTab2.Image != null)
            {
                pictureBoxLegend.Visible = true;

                if (listBox4Tab2MultipleSlices.SelectedItems.Count > 0)
                {
                    if (listBox1Tab2MZList.SelectedItems.Count > 0)
                    {
                        string sliceName = listBox4Tab2MultipleSlices.SelectedItem.ToString();

                        if (!checkBox1Tab2SelectROI.Checked)
                        {
                            if (rectFilterWithinDict.Keys.Contains(sliceName))
                            {
                                foreach (Rectangle Rect in rectFilterWithinDict[sliceName])
                                {
                                    e.Graphics.FillRectangle(selectionBrushWithin, Rect);
                                }
                            }

                            if (rectFilterOutsideDict.Keys.Contains(sliceName))
                            {
                                foreach (Rectangle Rect in rectFilterOutsideDict[sliceName])
                                {
                                    e.Graphics.FillRectangle(selectionBrushOutside, Rect);
                                }
                            }
                        }
                        else
                        {
                            if (radioButton3Tab2Dragging.Checked == true)
                            {
                                if (Rect.Width > 0 && Rect.Height > 0)
                                {
                                    if (LeftMousePressed == true)
                                    {
                                        e.Graphics.FillRectangle(selectedROIColor, Rect);
                                    }
                                }
                            }

                            // show the selected ROI within the slice
                            if (ROIRectListDict.Keys.Contains(sliceName))
                            {
                                foreach (Rectangle Rect in ROIRectListDict[sliceName])
                                {
                                    e.Graphics.FillRectangle(selectedROIColor, Rect);
                                }
                            }


                            if (listBox3Tab2ROI.SelectedItems.Count > 0)
                            {
                                if (highlightedRecListDict.Item1 == sliceName)
                                {
                                    if (highlightedRecListDict.Item2.Count > 0)
                                    {
                                        foreach (Rectangle highlightedRec in highlightedRecListDict.Item2)
                                        {
                                            e.Graphics.FillRectangle(selectionBrushHighlight, highlightedRec);
                                            e.Graphics.DrawRectangle(_Pen, highlightedRec);
                                        }
                                    }
                                }
                            }

                            if (rectGroupSliceDataDict.Count > 0)
                            {
                                foreach (ConcurrentDictionary<string, List<(int, Rectangle)>> rectList in rectGroupSliceDataDict.Values)
                                {
                                    if (rectList.Keys.Contains(sliceName))
                                    {
                                        foreach ((int, Rectangle) Rect in rectList[sliceName])
                                        {
                                            e.Graphics.FillRectangle(selectionBrushGroup, Rect.Item2);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                pictureBoxLegend.Visible = false;
            }
        }

        /// <summary>
        /// Filter out the mz in the listBox1Tab2 if the outside/inside > 2.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Button3Tab2StartFiltering_Click(object sender, EventArgs e)
        {
            if (listBox1Tab2MZList.Items.Count > 0)
            {
                listBox1Tab2MZList.Items.Clear();
            }

            if (listBox4Tab2MultipleSlices.SelectedItems.Count > 0)
            {
                string sliceName = listBox4Tab2MultipleSlices.SelectedItem.ToString();
                textBox1Tab2.AppendText("Start to filter the background m/z for slice " + sliceName + newLine);

                double stretchRatio = stretchRatioXYDict[sliceName];
                bool widerOrHigher = widerOrHigherDict[sliceName];

                int mzNumberBeforeFiltering = mZAndIntensityDict[sliceName].Count;

                using StreamWriter f = new(rootDirectoryLocation + @"\mz excluded since the within and outside in " + sliceName + ".txt");
                f.WriteLine("m/z" + "\t" + "within intensity" + "\t" + "outside intensity");

                if (rectFilterWithinDict[sliceName].Count > 0 && rectFilterOutsideDict[sliceName].Count > 0)
                {
                    for (int index = mZAndIntensityDict[sliceName].Count - 1; index >= 0; index--)
                    {
                        KeyValuePair<double, ConcurrentDictionary<int, double[]>> mZ = mZAndIntensityDict[sliceName].ElementAt(index);

                        double outsideFinalAvg = AFAMassIntensityPipeline.FromRectangleToData_v2(rectFilterOutsideDict, widerOrHigher,
                            sliceName, pictureBoxTab2, stretchRatio,
                            originalToPictureBoxRatioDict[sliceName], rawFileNumberDict, colNumDict, mZ.Value);
                        double withinFinalAvg = AFAMassIntensityPipeline.FromRectangleToData_v2(rectFilterWithinDict, widerOrHigher,
                            sliceName, pictureBoxTab2, stretchRatio,
                            originalToPictureBoxRatioDict[sliceName], rawFileNumberDict, colNumDict, mZ.Value);

                        // only remove items from mZAndHeatbinDict and the list but not the mZAndHeatmapDict mZAndMaxMinIntensityDict 
                        if (withinFinalAvg != 0)
                        {
                            if (outsideFinalAvg / withinFinalAvg > 2)
                            {
                                f.WriteLine(mZ.Key + "\t" + withinFinalAvg + "\t" + outsideFinalAvg);
                                mZAndIntensityDict[sliceName].TryRemove(mZ.Key, out _);
                            }
                        }
                        else
                        {
                            f.WriteLine(mZ.Key + "\t" + withinFinalAvg + "\t" + outsideFinalAvg);
                            mZAndIntensityDict[sliceName].TryRemove(mZ.Key, out _);
                        }
                    }
                    // the name of the slice could be highlighted after filtering
                    // need to change the listbox to listview
                    List<double> mzList = mZAndIntensityDict[sliceName].Keys.ToList().OrderBy(k => k).ToList();
                    foreach (double mz in mzList)
                    {
                        listBox1Tab2MZList.Items.Add(mz);
                    }
                }
                else
                {
                    MessageBox.Show("Please make within and outside selections before filtering");
                    return;
                }
                int mzNumberAfterFiltering = mZAndIntensityDict[sliceName].Count;
                textBox1Tab2.AppendText("Filtering complete for " + sliceName + ", the filtered out m/z and inside/outside intensities were stored in " + rootDirectoryLocation + newLine);
                textBox1Tab2.AppendText(sliceName + " remains " + mzNumberAfterFiltering + " m/z." + newLine);
            }
        }


        // the path to store filtered mz and intensities heatbins for the selected slice
        private string exportLocation2 = "";

        /// <summary>
        /// export the filtered mz and intensities heatbins for the selected MSI data
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void button3Tab2ExportRawData_MouseClick(object sender, MouseEventArgs e)
        {
            CommonOpenFileDialog dialog = new()
            {
                //InitialDirectory = currentDirectory,
                IsFolderPicker = true
            };

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                exportLocation2 = dialog.FileName + @"\";
            }
            else
            {
                return;
            }

            string writeCol = "mz_Value";

            if (listBox4Tab2MultipleSlices.SelectedItems.Count > 0)
            {
                Stopwatch singleTimeToExport = new Stopwatch();
                singleTimeToExport.Start();
                string index = listBox4Tab2MultipleSlices.SelectedItem.ToString();

                textBox1Tab2.AppendText("Start to export filtered results for slice: " + index + "." + newLine);

                int colMin = colNumDict[index];

                for (int i = 0; i < colMin; i++)
                {
                    writeCol = writeCol + "\t" + "intensity" + (i + 1);
                }

                string[] eachSlicesDataDirectory = Directory.GetDirectories(exportLocation2, "*", System.IO.SearchOption.TopDirectoryOnly);
                string eachSlicesDataDirectoryName = exportLocation2 + @"\" + index + "_txt_export";

                if (!eachSlicesDataDirectory.Contains(eachSlicesDataDirectoryName))
                {
                    _ = Directory.CreateDirectory(eachSlicesDataDirectoryName);
                }
                else
                {
                    DirectoryInfo di = new(eachSlicesDataDirectoryName);
                    foreach (FileInfo file in di.EnumerateFiles())
                    {
                        file.Delete();
                    }
                }
                await System.Threading.Tasks.Task.Run(() =>
                {
                    AFAMassIntensityPipeline.WriteAFAData_v2(mZAndIntensityDict[index], eachSlicesDataDirectoryName + @"\", writeCol, rawFileNumberDict[index], colMin);

                });
                singleTimeToExport.Stop();
                textBox1Tab2.AppendText("Complete export filtered results for slice: " + index + ". Time: " +
                    Math.Round(Convert.ToDouble(singleTimeToExport.ElapsedMilliseconds / 1000), 2) + "s." +
                    newLine);
                singleTimeToExport.Reset();
            }
            else
            {
                MessageBox.Show("Please select the MSI data to export first.");
                return;
            }
        }

        ConcurrentDictionary<double, double[]> mZROIIntensityDict = new ConcurrentDictionary<double, double[]>();

        /// <summary>
        /// Match ROI with the mean intensities of each m/z.
        /// If multiple slices were chosen, the m/z does not exist across the slices would not be 0 across the MSI experiment.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void button6Tab2MatchROIWithIntensity(object sender, EventArgs e)
        {
            int exportROINumTotal = 0; // the sum of all ROIs in all groups

            if (mZROIIntensityDict.Count > 0)
            {
                mZROIIntensityDict.Clear();
            }

            for (int i = 0; i < exportROINumEachGroup.Count; i++)
            {
                exportROINumTotal += exportROINumEachGroup.ElementAt(i).Value;
            }

            textBox1Tab2.AppendText("Start to match the ROI intensity data in each group." + newLine);
            //textBox1Tab2.AppendText("For each remaining m/z, selected data will underwent " + listBox4Tab2NormalMethod.Text + " and " + listBox5Tab2NormMethod2.Text + " data transformation methods" + newLine);

            List<double> allmZinDict = new();

            for (int p = 0; p < rectGroupSliceDataDict.Count; p++)
            {
                ConcurrentDictionary<string, List<(int, Rectangle)>> sliceAndROI = rectGroupSliceDataDict.ElementAt(p).Value;

                for (int j = 0; j < sliceAndROI.Count; j++)
                {
                    string sliceName = sliceAndROI.ElementAt(j).Key;
                    allmZinDict.AddRange(mZAndIntensityDict[sliceName].Keys);
                }
            }
            allmZinDict = allmZinDict.Distinct().ToList();

            foreach (double mZ in allmZinDict)
            {
                double[] exportIntensity = new double[exportROINumTotal];

                int index = 0;

                for (int p = 0; p < rectGroupSliceDataDict.Count; p++)
                {
                    ConcurrentDictionary<string, List<(int, Rectangle)>> sliceAndROI = rectGroupSliceDataDict.ElementAt(p).Value;
                    //foreach slice
                    for (int j = 0; j < sliceAndROI.Count; j++)
                    {
                        string sliceName = sliceAndROI.ElementAt(j).Key;
                        bool widerOrHigher = widerOrHigherDict[sliceName];
                        double stretchRatio = stretchRatioXYDict[sliceName];

                        //foreach ROI in the slice
                        for (int z = 0; z < sliceAndROI[sliceName].Count; z++)
                        {
                            Rectangle Rect = sliceAndROI[sliceName][z].Item2;
                            if (mZAndIntensityDict[sliceName].Keys.Contains(mZ))
                            {
                                exportIntensity[index] = AFAMassIntensityPipeline.SumTheIntensity(mZAndIntensityDict[sliceName][mZ],
                                    pictureBoxTab2, originalToPictureBoxRatioDict[sliceName], rawFileNumberDict[sliceName], colNumDict[sliceName], stretchRatio,
                                    Rect, widerOrHigher);
                            }
                            else
                            {
                                exportIntensity[index] = 0;
                            }
                            index++;
                        }
                    }
                }
                mZROIIntensityDict.TryAdd(mZ, exportIntensity.ToArray());
            }
            textBox1Tab2.AppendText("Finishing matching data, ready to export." + newLine);
        }

        /// <summary>
        /// Export the ROI data into selected folder.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void button6ExportDataWithinROI_Click(object sender, EventArgs e)
        {

            SaveFileDialog save = new SaveFileDialog();
            save.DefaultExt = "txt";
            save.Filter = "(*.TXT)|*.txt";

            if (save.ShowDialog() == DialogResult.OK)
            {
                textBox1Tab2.AppendText("Start to extract ROI data into:" + save.FileName + newLine);

                string writeCol1 = "mz_Value";
                string writeCol2 = "mz_Value";

                // add the group information and the ROI-Slice information
                for (int p = 0; p < rectGroupSliceDataDict.Count; p++)
                {
                    string groupName = rectGroupSliceDataDict.ElementAt(p).Key;

                    ConcurrentDictionary<string, List<(int, Rectangle)>> sliceAndROI = rectGroupSliceDataDict.ElementAt(p).Value;

                    for (int j = 0; j < sliceAndROI.Count; j++)
                    {
                        string sliceName = sliceAndROI.ElementAt(j).Key;

                        for (int z = 0; z < sliceAndROI[sliceName].Count; z++)
                        {
                            writeCol1 = writeCol1 + "\t" + "ROI " + sliceAndROI[sliceName][z].Item1 + " in the " + sliceName + " slice.";
                            writeCol2 = writeCol2 + "\t" + groupName;
                        }
                    }
                }

                AFAMassIntensityPipeline.WriteROIData(mZROIIntensityDict, writeCol1, writeCol2, save.FileName);

                textBox1Tab2.AppendText("Finishing to extraction ROI data." + newLine);
            }
            else
            {
                return;
            }

        }


        private void ButtonTab2QC_Click(object sender, EventArgs e)
        {
            cbt.Install();
            // select the exported txt data
            OpenFileDialog ROIGroupDataFile = new()
            {
                Title = "Directroy containing ROI Group Data",
                Multiselect = false,
                Filter = "TXT文件|*.txt" //支持的文件格式
            };
            DialogResult result = ROIGroupDataFile.ShowDialog();

            if (result == DialogResult.OK)
            {
                textBox1Tab2.AppendText("Start processing the QC file: " + ROIGroupDataFile.FileName + newLine);

                engine.Evaluate("set.seed(123)");

                engine.SetSymbol("read_in_txt", engine.CreateCharacter(ROIGroupDataFile.FileName.Replace(@"\", @"/")));
                engine.SetSymbol("dir", engine.CreateCharacter(ROIGroupDataFile.FileName.Substring(0, ROIGroupDataFile.FileName.LastIndexOf(@"\") + 1).Replace(@"\", @"/")));

                engine.Evaluate("raw_data=read.delim(read_in_txt, header = T, stringsAsFactors = F)\r\nrownames(raw_data) = raw_data[,1]\r\nraw_data = raw_data[,-1]");
                engine.Evaluate("mz_name_check = lapply(rownames(raw_data)[-1], function(each_mz){\r\n  grepl(\"\\\\d.\\\\d\", each_mz)\r\n})");

                if (engine.Evaluate("length(mz_name_check) == 0").AsLogical().First())
                {
                    MessageBox.Show("Please give the data in the correct form." + newLine);
                    return;
                }
                else
                {
                    if (engine.Evaluate("(!any(!unlist(mz_name_check)))").AsLogical().First())
                    {
                        textBox1Tab2.AppendText("The mz names have been checked and proceed to the next step." + newLine);
                    }
                    else
                    {
                        MessageBox.Show("Please give the data in the correct form." + newLine);
                        return;
                    }
                }

                engine.Evaluate("group_name_check = lapply(raw_data[1,], function(each_group){\r\n  grepl(\"[Gg]roup\\\\d\", each_group)\r\n})");

                if (engine.Evaluate("length(group_name_check) == 0").AsLogical().First())
                {
                    MessageBox.Show("Please give the data in the correct form." + newLine);
                    return;
                }
                else
                {
                    if (engine.Evaluate("!any(!unlist(group_name_check))").AsLogical().First())
                    {
                        textBox1Tab2.AppendText("The group names have been checked and proceed to the next step." + newLine);
                    }
                    else
                    {
                        MessageBox.Show("Please give the data in the correct form." + newLine);
                        return;
                    }
                }

                engine.Evaluate("group_data = unlist(c(raw_data[1,]))\r\n");

                engine.Evaluate("colnames(raw_data) = raw_data[1,]\r\nraw_data = raw_data[-1,]\r\nlibrary(\"stats\")\r\nlibrary(\"FactoMineR\")\r\nraw_data2 = apply(raw_data,2, as.numeric)\r\nrownames(raw_data2) = rownames(raw_data)");
                engine.Evaluate("all_zero_cir = any(unlist(apply(raw_data2,2, function(xx){\r\n  all(xx == 0)\r\n})) == T)\r\n\r\nif(all_zero_cir){\r\n  all_zero_col = which(unlist(apply(raw_data2,2, function(xx){\r\n    all(xx == 0)\r\n  })) == T)\r\n  raw_data2[1,all_zero_col] = 1\r\n}");
                engine.Evaluate("raw_data_normalized <- scale(raw_data2)\r\ncorr_matrix <- cor(raw_data_normalized)\r\nhead(raw_data_normalized)\r\nraw_data.pca <- princomp(corr_matrix)\r\n\r\n# raw_data.pca$loadings[, 1:2]\r\n# 2*sd(raw_data.pca$loadings[, 1])\r\n\r\nraw_data_to_plot = data.frame(PC1 = raw_data.pca$loadings[, 1],\r\n                          Num = factor(seq(1:length(raw_data.pca$loadings[, 1]))))");

                engine.Evaluate("library(ggplot2)\r\np <- ggplot(raw_data_to_plot, aes(x=Num, y=PC1, group = 1, color = group_data)) + geom_line(linewidth= 0.8) + geom_point() + \r\n  ylim((-3*sd(raw_data.pca$loadings[, 1])),(3*sd(raw_data.pca$loadings[, 1]))) + \r\n  geom_hline(yintercept=-2*sd(raw_data.pca$loadings[, 1]), color = \"black\", linetype = \"dashed\")  +\r\n  geom_text(aes(0,-2*sd(raw_data.pca$loadings[, 1]),label = \"2std\", vjust = -1, hjust = -0.8),colour = \"black\",check_overlap = TRUE) + \r\n  geom_hline(yintercept=2*sd(raw_data.pca$loadings[, 1]), color = \"black\", linetype = \"dashed\")  +\r\n  geom_text(aes(0, 2*sd(raw_data.pca$loadings[, 1]),label = \"2std\", vjust = 1.8, hjust = -0.8),colour = \"black\",check_overlap = TRUE)+ \r\n  geom_hline(yintercept=-3*sd(raw_data.pca$loadings[, 1]), color = \"black\", linetype = \"dashed\")  +\r\n  geom_text(aes(0,-3*sd(raw_data.pca$loadings[, 1]),label = \"3std\", vjust = -1, hjust = -0.8),colour = \"black\",check_overlap = TRUE) + \r\n  geom_hline(yintercept=3*sd(raw_data.pca$loadings[, 1]), color = \"black\", linetype = \"dashed\") +\r\n  geom_text(aes(0,3*sd(raw_data.pca$loadings[, 1]),label = \"3std\", vjust = 1.9, hjust = -0.8),colour = \"black\",check_overlap = TRUE) + \r\n  labs(color = \"Group\")");

                engine.Evaluate("if(length(unique(group_data)) > 1){\r\nlibrary(patchwork)\r\n  library(\"factoextra\") \r\n  dat.pca <- PCA(as.data.frame(t(raw_data_normalized)), graph = F)\r\n  pca_p = fviz_pca_ind(dat.pca,\r\n               geom.ind = \"point\", # show points only (nbut not \"text\")\r\n               col.ind =  group_data, # color by groups \r\n               addEllipses = T,\r\n               legend.title = \"Groups\"\r\n  ) \r\n  p = p + pca_p\r\n}");
                engine.Evaluate("plot(p)");
                cbt.Uninstall();
            }
        }

        /// <summary>
        /// give the user the option the copy and paste their m/z into the box
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void listBoxSignificantMetabolites_KeyDown(object sender, KeyEventArgs e)
        {
            if ((e.KeyCode == Keys.V) && (e.Modifiers == Keys.Control))
            {
                string[] clipboardRows = Clipboard.GetText(TextDataFormat.UnicodeText).Split(new string[] { "\r\n" },
                                                                                                 StringSplitOptions.None);
                foreach (string clipboardRow in clipboardRows)
                {
                    if (clipboardRow != "")
                    {
                        listBoxSignificantMetabolites.Items.Add(clipboardRow);
                    }
                }
            }

            if (e.Control && e.KeyCode == Keys.C)
            {
                System.Text.StringBuilder copy_buffer = new System.Text.StringBuilder();
                foreach (object item in listBoxSignificantMetabolites.SelectedItems)
                    copy_buffer.AppendLine(item.ToString());
                if (copy_buffer.Length > 0)
                    Clipboard.SetText(copy_buffer.ToString());
            }
        }


        //==========================================================================================================================================================================================================
        //=========================================================== Tab 3 ========================================================================================================================================
        //==========================================================================================================================================================================================================






        /// <summary>
        /// Missing data handling, filtering, normalization, batch effect removing, and statistical analysis
        /// R value dir is the ROI directory
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void button6_Click_ProcessingROIData(object sender, EventArgs e)
        {

            if (listBoxSignificantMetabolites.Items.Count > 0)
            {
                listBoxSignificantMetabolites.Items.Clear();
            }

            // select the exported txt data
            OpenFileDialog ROIGroupDataFile = new()
            {
                Title = "Directroy containing ROI Group Data",
                Multiselect = false,
                Filter = "TXT文件|*.txt" //支持的文件格式
            };
            DialogResult result = ROIGroupDataFile.ShowDialog();

            if (result == DialogResult.OK)
            {
                textBox1Tab3.AppendText("Start processing the file: " + ROIGroupDataFile.FileName + newLine);

                #region read in the data and initial check
                engine.Evaluate("set.seed(123)");

                engine.SetSymbol("read_in_txt", engine.CreateCharacter(ROIGroupDataFile.FileName.Replace(@"\", @"/")));
                engine.SetSymbol("dir", engine.CreateCharacter(ROIGroupDataFile.FileName.Substring(0, ROIGroupDataFile.FileName.LastIndexOf(@"\") + 1).Replace(@"\", @"/")));

                engine.Evaluate("raw_data=read.delim(read_in_txt, header = T, stringsAsFactors = F)\r\nrownames(raw_data) = raw_data[,1]\r\nraw_data = raw_data[,-1]");
                engine.Evaluate("mz_name_check = lapply(rownames(raw_data)[-1], function(each_mz){\r\n  grepl(\"\\\\d.\\\\d\", each_mz)\r\n})");

                if (engine.Evaluate("length(mz_name_check) == 0").AsLogical().First())
                {
                    MessageBox.Show("Please give the data in the correct form." + newLine);
                    return;
                }
                else
                {
                    if (engine.Evaluate("(!any(!unlist(mz_name_check)))").AsLogical().First())
                    {
                        textBox1Tab3.AppendText("The mz names have been checked and proceed to the next step." + newLine);
                    }
                    else
                    {
                        MessageBox.Show("Please give the data in the correct form." + newLine);
                        return;
                    }
                }

                engine.Evaluate("group_name_check = lapply(raw_data[1,], function(each_group){\r\n  grepl(\"[Gg]roup\\\\d\", each_group)\r\n})");

                if (engine.Evaluate("length(group_name_check) == 0").AsLogical().First())
                {
                    MessageBox.Show("Please give the data in the correct form." + newLine);
                    return;
                }
                else
                {
                    if (engine.Evaluate("!any(!unlist(group_name_check))").AsLogical().First())
                    {
                        textBox1Tab3.AppendText("The group names have been checked and proceed to the next step." + newLine);
                    }
                    else
                    {
                        MessageBox.Show("Please give the data in the correct form." + newLine);
                        return;
                    }
                }

                engine.Evaluate("group_data = unlist(c(raw_data[1,]))\r\nslice_data = sub(\"[.]\",'',sub(\"[.]\",\" \",sub(\"ROI[.][0-9][.]in[.]the[.]\", \"\", colnames(raw_data))) )\r\nraw_data = raw_data[-1,]\r\nraw_data2 = as.data.frame(apply(raw_data,2,as.numeric))\r\nrownames(raw_data2) = rownames(raw_data)");
                #endregion

                #region missing processing

                if (checkBoxFilterNAPercent.Checked)
                {
                    if (int.TryParse(textBoxMissingPercent.Text, out int missing_threshold))
                    {
                        if (missing_threshold < 100 & missing_threshold > 0)
                        {
                            engine.SetSymbol("missing_threshold", engine.CreateInteger(missing_threshold));

                            engine.Evaluate("raw_data_filtering1 = apply(raw_data, 1, function(each_row){\r\n  ((sum(each_row == 0))/length(each_row)) < (missing_threshold/100)\r\n})");
                            engine.Evaluate("raw_data_filtered = raw_data2[raw_data_filtering1,]");

                            if (engine.Evaluate("nrow(raw_data_filtered) == 0").AsLogical().First())
                            {
                                MessageBox.Show("No m/z passed the current missing threshold, please change.");
                                return;
                            }

                        }
                        else
                        {
                            MessageBox.Show("Please use the integer between 0 and 100");
                            return;
                        }
                    }
                    else
                    {
                        MessageBox.Show("Please use a integer number as the percentage threshold." + newLine);
                        return;
                    }
                    textBox1Tab3.AppendText("The metabolites with more than " + missing_threshold + "% missing were filtered out." + newLine);
                }
                //else
                //{
                //    if (!(listBoxFillMissingMethods.SelectedItems.Count > 0))
                //    {
                //        MessageBox.Show("Please use at least a missing filling method provided above");
                //        return;
                //    }
                //}

                if (listBoxFillMissingMethods.SelectedItems.Count > 0)
                {
                    engine.SetSymbol("NA_processing_method", engine.CreateInteger(listBoxFillMissingMethods.SelectedIndex + 1));
                    textBox1Tab3.AppendText("NA processing method was chosen as: " + listBoxFillMissingMethods.SelectedItem + newLine);

                    engine.Evaluate("raw_data_filtered2 = raw_data_filtered");
                    engine.Evaluate("library(mice)");
                    engine.Evaluate("library(stats) \r\n if(NA_processing_method ==1){\r\n  for (i in 1:nrow(raw_data_filtered2))\r\n  {raw_data_filtered2[i,][raw_data_filtered2[i,] == 0] = min(raw_data_filtered2[i,][!raw_data_filtered2[i,] == 0])/5  }\r\n} else if(NA_processing_method ==2){\r\n  for (i in 1:nrow(raw_data_filtered2))\r\n  {raw_data_filtered2[i,][raw_data_filtered2[i,] == 0] = mean(raw_data_filtered2[i,][!raw_data_filtered2[i,] == 0])  }\r\n} else if(NA_processing_method ==3){\r\n  for (i in 1:nrow(raw_data_filtered2))\r\n  {raw_data_filtered2[i,][raw_data_filtered2[i,] == 0] = median(raw_data_filtered2[i,][!raw_data_filtered2[i,] == 0])  }\r\n} else if(NA_processing_method ==4){\r\n  for (i in 1:nrow(raw_data_filtered2))\r\n  {raw_data_filtered2[i,][raw_data_filtered2[i,] == 0] = min(raw_data_filtered2[i,][!raw_data_filtered2[i,] == 0])  }\r\n} else if(NA_processing_method ==5){\r\n  for (i in 1:nrow(raw_data_filtered2))\r\n  {raw_data_filtered2[i,][raw_data_filtered2[i,] == 0] = NA }\r\n  impute_results = tryCatch(mice(raw_data_filtered2, m=5,maxit=50,meth='pmm',seed=500),\r\n                            error = function(cond) {\r\n                              return(FALSE)\r\n                            })\r\n  if(impute_results){raw_data_filtered2 = complete(impute_results, 1)}else{\r\n    raw_data_filtered2 = raw_data_filtered\r\n  }} else if(NA_processing_method ==6){\r\n  for (i in 1:nrow(raw_data_filtered2))\r\n  {raw_data_filtered2[i,][raw_data_filtered2[i,] == 0] = NA }\r\n  impute_results = mice(raw_data_filtered2, m=5,maxit=50,meth='rf',seed=500)\r\n  raw_data_filtered2 = complete(impute_results, 1)\r\n} else if(NA_processing_method ==7){\r\n  raw_data_filtering2 = apply(raw_data_filtered, 1, function(each_row){\r\n    (any(each_row == 0))\r\n  })\r\n  raw_data_filtered2 = raw_data_filtered2[!raw_data_filtering2,]\r\n}");
                }
                else
                {
                    MessageBox.Show("Please use at least a missing filling method provided in Step 2.");
                    return;
                }

                LogicalVector anyNAremaining = engine.Evaluate("any(is.na(raw_data_filtered2))").AsLogical();

                if (anyNAremaining[0])
                {
                    MessageBox.Show("Please use another missing filling method provided in Step 2.");
                    return;
                }

                textBox1Tab3.AppendText("The missing values were handled." + newLine);
                #endregion

                #region filtering processing
                // will only use the top 5k or the 80% of the m/zs for the following analysis

                if (listBoxDataFilteringMethods.SelectedItems.Count > 0)
                {
                    engine.SetSymbol("data_filter_method", engine.CreateInteger(listBoxDataFilteringMethods.SelectedIndex + 1));
                    textBox1Tab3.AppendText("Data filtering processing method was chosen as: " + listBoxDataFilteringMethods.SelectedItem + newLine);

                }
                else
                {
                    engine.SetSymbol("data_filter_method", engine.CreateInteger(1));
                    textBox1Tab3.AppendText("Data filtering processing method was chosen as: None" + newLine);
                }
                engine.Evaluate("library(stats) \r\n if(data_filter_method == 1){\r\n  raw_data_filtered3 = raw_data_filtered2\r\n} else {\r\n  if (data_filter_method == 2){\r\n    filter.val <- apply(raw_data_filtered2, 1, IQR, na.rm=T);\r\n  }else if (data_filter_method ==3){\r\n    filter.val <- apply(raw_data_filtered2, 1, sd, na.rm=T);\r\n  }else if (data_filter_method ==4){\r\n    filter.val <- apply(raw_data_filtered2, 1, mad, na.rm=T);\r\n  } else if (data_filter_method ==5){\r\n    sds <- apply(raw_data_filtered2, 1, sd, na.rm=T);\r\n    mns <- apply(raw_data_filtered2, 1, mean, na.rm=T);\r\n    filter.val <- abs(sds/mns);\r\n  }else if (data_filter_method ==6 ){\r\n    mads <- apply(raw_data_filtered2, 1, mad, na.rm=T);\r\n    meds <- apply(raw_data_filtered2, 1, median, na.rm=T);\r\n    filter.val <- abs(mads/meds);\r\n  }else if (data_filter_method ==7){\r\n    filter.val <- apply(raw_data_filtered2, 1, mean, na.rm=T);\r\n  }else if (data_filter_method ==8){\r\n    filter.val <- apply(raw_data_filtered2, 1, median, na.rm=T);\r\n  }\r\n  \r\n  rk <- rank(-filter.val, ties.method='random') # rank is small to large,\r\n  \r\n  if(nrow(raw_data_filtered2) > 5000) {\r\n    raw_data_filtered3 = raw_data_filtered2[rk<5000,]\r\n  } else {\r\n    raw_data_filtered3 = raw_data_filtered2[rk<nrow(raw_data_filtered2) * 0.8,]\r\n  }\r\n}");

                #endregion

                #region Nomalization methods
                if (listBoxSampleWiseNorm.SelectedItems.Count > 0)
                {
                    engine.SetSymbol("sample_norm", engine.CreateInteger(listBoxSampleWiseNorm.SelectedIndex + 1));
                    textBox1Tab3.AppendText("1st normalization method was chosen as: " + listBoxSampleWiseNorm.SelectedItem + newLine);
                }
                else
                {
                    engine.SetSymbol("sample_norm", engine.CreateInteger(1));
                    textBox1Tab3.AppendText("1st normalization method was chosen as: None" + newLine);
                }


                if (listBoxTransformNorm.SelectedItems.Count > 0)
                {
                    engine.SetSymbol("transform_norm", engine.CreateInteger(listBoxTransformNorm.SelectedIndex + 1));
                    textBox1Tab3.AppendText("2nd normalization method was chosen as: " + listBoxTransformNorm.SelectedItem + newLine);
                }
                else
                {
                    engine.SetSymbol("transform_norm", engine.CreateInteger(1));
                    textBox1Tab3.AppendText("2nd normalization method was chosen as: None" + newLine);
                }

                if (listBoxMZwiseProcessing.SelectedItems.Count > 0)
                {
                    engine.SetSymbol("mz_wise_norm", engine.CreateInteger(listBoxMZwiseProcessing.SelectedIndex + 1));
                    textBox1Tab3.AppendText("3rd normalization method was chosen as: " + listBoxMZwiseProcessing.SelectedItem + newLine);
                }
                else
                {
                    engine.SetSymbol("mz_wise_norm", engine.CreateInteger(1));
                    textBox1Tab3.AppendText("3rd normalization method was chosen as: None" + newLine);
                }

                engine.Evaluate("library(preprocessCore)\r\ndata <- preprocessCore::normalize.quantiles(as.matrix(raw_data_filtered3))\r\nrownames(data) = rownames(raw_data_filtered3)\r\ncolnames(data) = colnames(raw_data_filtered3)\r\nvarRow <- apply(data, 1, var, na.rm = T) \r\nconstRow <- (varRow == 0 | is.na(varRow))\r\nconstNum <- sum(constRow, na.rm = T)");


                // 20221231 potential bug: positive length when the group set to 2???
                engine.Evaluate("data <- data[!constRow, ]\r\ncol_ROI_Names <- colnames(data)\r\nrow_mz_Names <- rownames(data)");
                int number_of_row = engine.Evaluate("nrow(data)").AsInteger().ToList().First();
                textBox1Tab3.AppendText("After filtering process, " + number_of_row + " m/z remain." + newLine);

                engine.Evaluate("varRow <- apply(data, 1, var, na.rm = T) \r\nif(nrow(data) < 10 ){\r\n  mz_to_plot = rownames(data)\r\n  mz_to_plot_100 = rownames(data)\r\n}else{\r\n  if(nrow(data) < 100){\r\n    mz_to_plot = names(rank(-varRow, ties.method = \"average\")[1:10])\r\n    mz_to_plot_100 = rownames(data)\r\n  }else{\r\n    mz_to_plot = names(rank(-varRow, ties.method = \"average\")[1:10])\r\n    mz_to_plot_100 =names(rank(-varRow, ties.method = \"average\")[1:100])\r\n  }\r\n}");

                engine.Evaluate("\r\nif(sample_norm == 1) {\r\n  raw_data_filtered4 = data\r\n} else if(sample_norm == 2) {\r\n  raw_data_filtered4 <- apply(data, 2, function(x){\r\n    1000000*x/sum(x, na.rm=T);\r\n  })\r\n} else if(sample_norm == 3){\r\n  raw_data_filtered4 <- apply(data, 2, function(x){\r\n    x/median(x, na.rm=T);\r\n  })\r\n}\r\nmin.val <- min(abs(raw_data_filtered4[raw_data_filtered4 != 0]))/10\r\nif(transform_norm == 1) {\r\n  raw_data_filtered5 = t(raw_data_filtered4)\r\n} else if(transform_norm == 2) {\r\n  raw_data_filtered5 <- apply(raw_data_filtered4, 1, function(x){\r\n    log10((x + sqrt(x^2 + min.val^2))/2)\r\n  })\r\n} else if(transform_norm == 3){\r\n  raw_data_filtered5 <- apply(raw_data_filtered4, 1, function(x){\r\n    ((x + sqrt(x^2 + min.val^2))/2)^(1/2);\r\n  })\r\n} else if(transform_norm == 4){\r\n  raw_data_filtered5 <- apply(raw_data_filtered4, 1, function(x){\r\n    ((x + sqrt(x^2 + min.val^2))/2)^(1/3);\r\n  })\r\n}\r\n#raw_data_filtered5 was t() transposed\r\n\r\nif(mz_wise_norm == 1){\r\n  raw_data_filtered6 = raw_data_filtered5\r\n} else if(mz_wise_norm == 2){\r\n  raw_data_filtered6 =apply(raw_data_filtered5, 2, function(x){\r\n    (x - mean(x))/sd(x, na.rm=T);\r\n  })\r\n} else if(mz_wise_norm ==3) {\r\n  raw_data_filtered6 =apply(raw_data_filtered5, 2, function(x){\r\n    (x - mean(x))/sqrt(sd(x, na.rm=T));\r\n  })\r\n}else if(mz_wise_norm ==4) {\r\n  raw_data_filtered6 =apply(raw_data_filtered5, 2, function(x){\r\n    x - mean(x);\r\n  })\r\n}else if(mz_wise_norm ==5) {\r\n  raw_data_filtered6 =apply(raw_data_filtered5, 2, function(x){\r\n    if(max(x) == min(x)){\r\n      x;\r\n    }else{\r\n      (x - mean(x))/(max(x)-min(x));\r\n    }\r\n  })\r\n}\r\nraw_data_filtered6 = t(raw_data_filtered5)\r\n\r\nrownames(raw_data_filtered6) = row_mz_Names\r\ncolnames(raw_data_filtered6) = col_ROI_Names");

                #endregion

                #region plot the data before and after normalized
                engine.Evaluate("library(ggplot2)\r\nlibrary(tidyr)\r\nlibrary(ggridges)\r\nlibrary(forcats)\r\nbefore_normalization_data = raw_data_filtered3[mz_to_plot,]\r\nbefore_normalization_data[\"mz\"]  = rownames(before_normalization_data)\r\nbefore_normalization_plot_data = gather(before_normalization_data,\"sample\",\"Intensity\",-mz)\r\n# ggplot(before_normalization_plot_data,aes(x = mz,y = Intensity, fill = mz)) + \r\n#   geom_violin(trim = F, scale = \"width\") + \r\n#   theme_bw()  + coord_flip()+ labs( x = \"m/z\", y = \"Intensity before normalization\")\r\n\rfigure1a_before_norm = ggplot(data=before_normalization_plot_data, aes(x=Intensity, y= mz,fill=fct_rev(mz)))+\r\n  geom_density_ridges(alpha=0.4,\r\n                      bandwidth=1,\r\n                      scale=0.6)+ \r\n  labs( y = \"m/z\", title = \"Intensity before normalization\")+\r\n  theme(legend.position=\"none\", plot.title = element_text(hjust = 0.5))\r\n\r\nafter_normalization_data = data.frame(raw_data_filtered6[mz_to_plot,])\r\nafter_normalization_data[\"mz\"]  = rownames(after_normalization_data)\r\nafter_normalization_plot_data = gather(after_normalization_data,\"sample\",\"Intensity\",-mz)\r\n# ggplot(after_normalization_plot_data,aes(x = mz,y = Intensity, fill = mz)) + \r\n#   geom_violin(trim = F, scale = \"width\") + \r\n#   theme_bw()  + coord_flip() + labs( x = \"m/z\", y = \"Intensity after normalization\")\r\nfigure1b_after_norm = ggplot(data=after_normalization_plot_data, aes(x=Intensity, y= mz,fill=fct_rev(mz)))+\r\n  geom_density_ridges(alpha=0.4,\r\n                      bandwidth=1,\r\n                      scale=0.6)+ \r\n  labs( y = \"\", title = \"Intensity after normalization\", fill = \"m/z\")+\r\n  theme(plot.title = element_text(hjust = 0.5))\r\nlibrary(patchwork)\r\nfigure1a_before_norm + figure1b_after_norm\r\nggsave(filename = paste(dir, \"normalization_ridge.pdf\", sep = \"\"),width = 8,height = 4,units = \"in\")");
                textBox1Tab3.AppendText("The ridge plots were plotted for visualizing." + newLine);
                engine.Evaluate("before_normalization_data_100 = data.frame(Intensity = unlist(as.vector(raw_data_filtered3[mz_to_plot_100,])))\r\n\r\nafter_normalization_data_100 = data.frame(Intensity = unlist(as.vector(raw_data_filtered6[mz_to_plot_100,])))\r\n\r\nbefore_normalization_data_100_p = ggplot(before_normalization_data_100, aes(x = Intensity)) + \r\n  geom_histogram(aes(y =after_stat(density)), \r\n                 colour = \"grey\", \r\n                 fill = \"grey\")  + geom_density(linewidth = 0.7) + theme_classic() + labs( y = \"Density\", x = \"Before normalization intensity\")\r\n\r\nafter_normalization_data_100_p = ggplot(after_normalization_data_100, aes(x = Intensity)) + \r\n  geom_histogram(aes(y =after_stat(density)), \r\n                 colour = \"grey\", \r\n                 fill = \"grey\")  + geom_density(linewidth = 0.7) + theme_classic() + labs( y = \"Density\", x= \"After normalization intensity\")\r\n\r\nggsave(plot = before_normalization_data_100_p + after_normalization_data_100_p , \r\n       filename = paste(dir, \"normalization_density.pdf\", sep = \"\"),\r\n       width = 8,height = 3,units = \"in\")");
                textBox1Tab3.AppendText("The density plot was generated." + newLine);
                #endregion

                #region analysis the data and filter the results

                engine.SetSymbol("remove_batch_effect", engine.CreateLogical(checkBoxBatchEffect.Checked));

                engine.Evaluate("library(ropls)\r\nlibrary(limma)\r\nlibrary(\"FactoMineR\")\r\nlibrary(\"factoextra\")");
                if (checkBoxBatchEffect.Checked)
                {
                    if (engine.Evaluate("any(table(factor(group_data)) == 1) | any(length(unique(slice_data))== 1)").AsLogical().First())
                    {
                        textBox1Tab3.AppendText("Could not perform the batch removing for the current data" + newLine);
                        engine.Evaluate("remove_batch_effect = F\r\n    raw_data_filtered7 = t(raw_data_filtered6)");
                    }
                    else
                    {
                        engine.Evaluate("design=model.matrix(~factor(group_data))\r\n    ex_b_limma <- removeBatchEffect(raw_data_filtered6,\r\n                                    batch = slice_data,\r\n                                    design = design)\r\n    raw_data_filtered7 = t(ex_b_limma)");
                    }
                }
                else
                {
                    engine.Evaluate("raw_data_filtered7 = t(raw_data_filtered6)");
                }
                engine.Evaluate("p_val_list <- c()");

                if (engine.Evaluate("length(unique(group_data)) > 1").AsLogical().First())
                {
                    engine.Evaluate("if (!requireNamespace(\"mixOmics\", quietly = TRUE)) {\r\n  BiocManager::install(\"mixOmics\")\r\n} else { \r\n library(mixOmics) \r\n}");
                    engine.Evaluate("if(remove_batch_effect){\r\n    df_pca_after <- PCA(raw_data_filtered7, graph = FALSE)\r\n    figure_df_pca_after_plot = fviz_pca_ind(df_pca_after, addEllipses = T,\r\n  col.ind = group_data ,labelsize = 3,\r\n                                            show.legend=FALSE,\r\n                                            repel = T,\r\n                                            legend.title = \"Groups\")+\r\n      theme(plot.title = element_text(hjust = 0.5)) + \r\n      labs(title = \"PCA after batch effect removing\")\r\n    \r\n    df_pca_before <- PCA(t(raw_data_filtered6), graph = FALSE)\r\n    figure_df_pca_before_plot = fviz_pca_ind(df_pca_before, addEllipses = T,\r\n                                             col.ind = group_data ,labelsize = 3,\r\n                                             show.legend=FALSE,\r\n                                             repel = T)+\r\n      theme(legend.position = \"none\", plot.title = element_text(hjust = 0.5)) + \r\n      labs(title = \"PCA before batch effect removing\") \r\n }else{ \r\n pdf(file = paste(dir,  \"PCA_Plot.pdf\", sep = \"\"), width = 7, height = 5)\r\n    plotIndiv(pca(t(raw_data_filtered6), center = TRUE, scale = TRUE), \r\n              group = factor(group_data),ellipse = TRUE,\r\n              ind.names = FALSE, # plot the samples projected\r\n              legend = TRUE, title = 'PCA')\r\n    dev.off() \r\n  background = background.predict(splsda(t(raw_data_filtered6),factor(group_data) ), comp.predicted=2, dist = \"max.dist\")\r\n    pdf(file = paste(dir,  \"PLSDA_Plot.pdf\", sep = \"\"), width = 7, height = 5)\r\n    plotIndiv(splsda(t(raw_data_filtered6),factor(group_data) ), comp = 1:2,\r\n              group = factor(group_data) , ind.names = FALSE, # colour points by class\r\n              background = background, # include prediction background for each class\r\n              legend = TRUE, title = \"PLSDA with prediction background\")\r\n    dev.off()}");


                    engine.Evaluate("if(nrow(raw_data_filtered7) < 7){\r\n    crossvalI = nrow(raw_data_filtered7)\r\n  } else{\r\n    crossvalI = 7 # the default value\r\n  }");
                    //if group number equals 2
                    if (engine.Evaluate("length(unique(group_data)) ==2").AsLogical().First())
                    {
                        engine.Evaluate("#OPLS + t_test\r\n    pdf(file = paste(dir, 'oplsda_model_building.pdf', sep = ''), height = 10, width = 10)\r\n    sacurine.oplsda <- opls(raw_data_filtered7, factor(group_data), orthoI = NA, predI = 1, crossvalI = crossvalI, algoC = \"default\")\r\n    dev.off()");
                        if (engine.Evaluate("!is.null(sacurine.oplsda@modelDF[1,1])").AsLogical().First())
                        {
                            textBox1Tab3.AppendText("OPLS model finished. (Group number =2)" + newLine);
                            engine.Evaluate("vipVn <- getVipVn(sacurine.oplsda)");
                        }
                        else
                        {
                            textBox1Tab3.AppendText("OPLS model was not built because the first predictive component was already not significant." + newLine);
                            textBox1Tab3.AppendText("VIP > 1 threshold would not be implemented." + newLine);
                        }
                        engine.Evaluate("raw_data_filtered7 = as.data.frame(raw_data_filtered7)\r\n    for(col_index in 1:ncol(raw_data_filtered7)){\r\n      if((var(raw_data_filtered7[group_data == \"Group1\",col_index]) == 0) \r\n         & (var(raw_data_filtered7[group_data == \"Group2\",col_index]) == 0)){\r\n        p_val_list[col_index] = 0\r\n      }else{\r\n        p_val_list[col_index] =round((t.test(raw_data_filtered7[,col_index] ~ group_data))$p.value , 2)\r\n      }\r\n    }");
                    }
                    //if group number larger than 2
                    else
                    {
                        engine.Evaluate("pdf(file = paste(dir, 'plsda_model_building.pdf', sep = ''), height = 10, width = 10)\r\n    sacurine.plsda <- opls(raw_data_filtered7, factor(group_data), crossvalI = crossvalI)\r\n    dev.off()");
                        if (engine.Evaluate("!is.null(sacurine.plsda@modelDF[1,1])").AsLogical().First())
                        {
                            textBox1Tab3.AppendText("PLS model finished. (Group number >2)" + newLine);
                            engine.Evaluate("vipVn <- getVipVn(sacurine.plsda)");
                        }
                        else
                        {
                            textBox1Tab3.AppendText("PLS model was not built because the first predictive component was already not significant." + newLine);
                            textBox1Tab3.AppendText("VIP > 1 threshold would not be implemented." + newLine);
                        }
                        engine.Evaluate("raw_data_filtered7 = as.data.frame(raw_data_filtered7)\r\n    p_val_list <- lapply(raw_data_filtered7, function(each_col){\r\n      anova_res <- aov(each_col ~ group_data)\r\n      anova_p_val <- round(summary(anova_res)[[1]][1,5],2)\r\n return(anova_p_val)\r\n    })");
                    }
                }
                else
                {
                    MessageBox.Show("Please give data with more than 1 group.");
                    return;
                }


                if (double.TryParse(textBox2Tab3PvalThreshold.Text, out double p_val_threshold))
                {
                    if (p_val_threshold < 1 & p_val_threshold > 0)
                    {
                        engine.SetSymbol("p_val_threshold", engine.CreateNumeric(p_val_threshold));
                        engine.Evaluate("int_1 = p_val_list < p_val_threshold");
                        textBox1Tab3.AppendText("Metabolites with p values greater than " + p_val_threshold + " were filtered out" + newLine);
                    }
                    else
                    {
                        MessageBox.Show("Please use the decimal between 0 and 1");
                        return;
                    }
                }
                else
                {
                    MessageBox.Show("Please use a number as the p value threshold." + newLine);
                    return;
                }

                engine.Evaluate("library(tidyverse)\r\nfiltered_mz_list = raw_data_filtered7 |> rbind(p_val_list) |> select_if(unlist(int_1))");

                if (engine.Evaluate("length(unique(group_data)) ==2").AsLogical().First())
                {

                    if (engine.Evaluate("!is.null(sacurine.oplsda@modelDF[1,1]) ").AsLogical().First())
                    {
                        engine.Evaluate("int_2 = vipVn[which(vipVn>1)] \r\n filtered_mz_list = filtered_mz_list[,colnames(filtered_mz_list) %in% names(int_2)]");
                    }
                    else
                    {
                        textBox1Tab3.AppendText("The OPLS statistical model was not built." + newLine);
                    }
                }
                else
                {
                    if (engine.Evaluate("!is.null(sacurine.plsda@modelDF[1,1])").AsLogical().First())
                    {
                        engine.Evaluate("int_2 = vipVn[which(vipVn>1)] \r\n filtered_mz_list = filtered_mz_list[,colnames(filtered_mz_list) %in% names(int_2)]");

                    }
                    else
                    {
                        textBox1Tab3.AppendText("The PLS statistical model was not built." + newLine);
                    }
                }

                // filtered_mz_list is a dataframe with m/z and intenties
                engine.Evaluate("if(remove_batch_effect)\r\n {figure_df_pca_before_plot +figure_df_pca_after_plot\r\nggsave(filename = paste(dir, \"PCA_plot.pdf\", sep = \"\"),width = 8,height = 4,units = \"in\")}\r\n");

                List<double> filtered_mz_list = engine.Evaluate("colnames(filtered_mz_list)").AsNumeric().ToList().OrderBy(k => k).ToList();

                foreach (double filtered_mz in filtered_mz_list)
                {
                    listBoxSignificantMetabolites.Items.Add(filtered_mz);
                }

                textBox1Tab3.AppendText("Final m/z were displayed in the box" + newLine);
                #endregion
            }
            else
            {
                return;
            }
        }

        /// <summary>
        /// show the boxplot and heatmap
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void buttonShowData_Click(object sender, EventArgs e)
        {
            cbt.Install();
            if (listBoxSignificantMetabolites.SelectedItems.Count > 0)
            {
                //string selectedMz = listBoxSignificantMetabolites.SelectedItem.ToString();
                double selectedMz = double.Parse(listBoxSignificantMetabolites.SelectedItem.ToString());
                //string selectedMz_string = double.Parse(listBoxSignificantMetabolites.SelectedItem.ToString()).ToString("F4");

                engine.SetSymbol("selected_mz", engine.CreateNumeric(selectedMz));
                //string[] a = engine.Evaluate("selected_mz").AsCharacter().ToArray();
                //textBox1Tab3.AppendText("selected_mz" + a[0] + newLine);

                engine.Evaluate("library(ggpubr)\r\nlibrary(ggsignif)\r\nlibrary(rstatix)\r\n\r\nintensities = raw_data_filtered7[colnames(raw_data_filtered7) %in% selected_mz]\r\nplot_data= data.frame(Intensity = raw_data_filtered7[,colnames(raw_data_filtered7) %in% selected_mz],\r\n                      group_data = group_data,\r\n                      check.names = F)");
                engine.Evaluate("colnames(plot_data)[2] = \"Groups\"");

                engine.Evaluate("if(remove_batch_effect){\r\n    df_pca_after <- PCA(raw_data_filtered7, graph = FALSE)\r\n    figure_df_pca_after_plot = fviz_pca_ind(df_pca_after,\r\n              col.ind = group_data ,labelsize = 3,\r\n             repel = T,max.overlaps = 10,\r\n                                            legend.title = \"Groups\")\r\n    df_pca_before <- PCA(t(raw_data_filtered6), graph = FALSE)\r\n    figure_df_pca_before_plot = fviz_pca_ind(df_pca_before,\r\n                                             col.ind = group_data ,\r\n                                             repel = T,max.overlaps = 10,\r\n                                             legend.title = \"Groups\")\r\n    \r\n  }");

                if (engine.Evaluate("length(unique(group_data)) ==2").AsLogical().First())
                {
                    engine.Evaluate("plot_data3 <- plot_data %>% \r\n    t_test(Intensity ~ Groups)%>%\r\n    adjust_pvalue() %>% add_significance(\"p.adj\")%>% \r\n    add_xy_position(x = \"Groups\")\r\n  figure_boxplot = ggplot(data = plot_data, aes(x=Groups, y=Intensity)) +\r\n    stat_boxplot(geom = \"errorbar\",width=0.15,aes(color=Groups))+ \r\n    geom_boxplot(aes(color=Groups),fill=\"white\")+\r\n    geom_jitter(aes(color=Groups,fill=Groups),width =0.05,shape = 21)+\r\n    theme_classic()+\r\n    scale_y_continuous(expand = expansion(mult = c(0, 0.15)))+\r\n    stat_pvalue_manual(plot_data3, label = \"p.adj.signif\", tip.length = 0.01, inherit.aes = FALSE) + \r\n    labs( x = paste(\"m/z:\",selected_mz))");
                }
                else
                {
                    engine.Evaluate("plot_data3 <- plot_data %>% \r\n    tukey_hsd(Intensity ~ Groups)%>% \r\n    add_xy_position(x = \"Groups\")\r\n  \r\n  figure_boxplot = ggplot(data = plot_data, aes(x=Groups, y=Intensity)) +\r\n    stat_boxplot(geom = \"errorbar\",width=0.15,aes(color=Groups))+ \r\n    geom_boxplot(aes(color=Groups),fill=\"white\")+\r\n    geom_jitter(aes(color=Groups,fill=Groups),width =0.05,shape = 21)+\r\n    theme_classic()+\r\n    scale_y_continuous(expand = expansion(mult = c(0, 0.1)))+\r\n    # theme_bw()+\r\n    stat_pvalue_manual(plot_data3, label = \"p.adj.signif\", tip.length = 0.01,\r\n                       inherit.aes = FALSE)+labs( x = paste(\"m/z:\",selected_mz))+ \r\n    labs( x = paste(\"m/z:\",selected_mz))");
                }
                engine.Evaluate("plot_list = list(figure_boxplot)");
                if (mZAndIntensityDict.Count > 0)
                {
                    foreach (KeyValuePair<string, ConcurrentDictionary<double, ConcurrentDictionary<int, double[]>>> slicemZAndHeatbin in mZAndIntensityDict)
                    {
                        engine.SetSymbol("sliceName", engine.CreateCharacter(slicemZAndHeatbin.Key));
                        string sliceName = slicemZAndHeatbin.Key;
                        int colTotal = colNumDict[sliceName];
                        int rawFileNumber = rawFileNumberDict[sliceName];

                        if (slicemZAndHeatbin.Value.ContainsKey(selectedMz))
                        {
                            ConcurrentDictionary<int, double[]> heatbin = slicemZAndHeatbin.Value[selectedMz];
                            engine.Evaluate("heatmapData <- list()");

                            for (int i = 0; i < rawFileNumber; i++)
                            {
                                double[] exportArrary = new double[colTotal];
                                if (heatbin.ContainsKey(i))
                                {
                                    for (int j = 0; j < heatbin[i].Length; j++)
                                    {
                                        exportArrary[j] = heatbin[i][j];
                                    }
                                }

                                NumericVector eachRow = engine.CreateNumericVector(exportArrary);
                                engine.SetSymbol("eachRow", eachRow);
                                engine.Evaluate("heatmapData <- append(heatmapData, as.data.frame(eachRow))");
                            }
                            engine.Evaluate("heatmapData <- Reduce(rbind, heatmapData)");
                            engine.Evaluate("library(ComplexHeatmap)\r\nlibrary(circlize)");

                            engine.Evaluate("seq_for_plot = seq(min(heatmapData), max(heatmapData),length = 6)\r\nseq_for_plot = c(seq_for_plot[1:3], mean(seq_for_plot[3], seq_for_plot[4]), seq_for_plot[4:6])\r\n");
                            engine.Evaluate("col_for_heatmap = colorRamp2(seq_for_plot, c(\"#0006c6\", \"#0059ff\",\"#00FFFF\", \"#00FF00\", \"#FFFF00\", \"#FF0000\",\"#9A0000\"))\r\n");
                            engine.Evaluate("heat_map =grid.grabExpr(draw(Heatmap(matrix = heatmapData,\r\n                                     col = col_for_heatmap,\r\n                                     cluster_rows = F,\r\n                                     cluster_columns = F,\r\n                                     show_row_names = F,\r\n                                     heatmap_legend_param = list(title = paste(\"Intensity\")),\r\n                                     column_title = paste('Heatmap for the \n', sliceName,'slice'), column_title_gp = gpar(fontsize = 10)\r\n)))");
                            engine.Evaluate("plot_list[[length(plot_list) + 1]] = heat_map\r\n");
                        }
                    }
                    engine.Evaluate("final_plot = plot_list[[1]]\r\n\r\nfor(i in 2:length(plot_list)){\r\n  final_plot = final_plot + plot_list[[i]]\r\n}");
                    engine.Evaluate("plot(final_plot)");
                }
                else
                {
                    MessageBox.Show("Please add the data before plotting the heatmaps");
                    return;
                }
            }
            else
            {
                MessageBox.Show("Please select the m/z first");
                return;
            }
            cbt.Uninstall();
        }

        string ROIexportLocation = "";
        /// <summary>
        /// export the filtered m/z and their p value
        /// export the thier intensity map (based on their current dictionary)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void buttonTab3Export_Click(object sender, EventArgs e)
        {
            SaveFileDialog save = new()
            {
                DefaultExt = "csv",
                Filter = "(*.CSV)|*.csv"
            };

            if (save.ShowDialog() == DialogResult.OK)
            {
                ROIexportLocation = Path.GetDirectoryName(save.FileName);
                string fileName = save.FileName;
                string firstRow = "";

                engine.SetSymbol("dir_to_save", engine.CreateCharacter(fileName.Replace(@"\", "/")));
                engine.Evaluate("rownames(filtered_mz_list)[nrow(filtered_mz_list)] =\"P_value\"\r\nwrite.csv(filtered_mz_list,file = dir_to_save,\r\n quote = F)");

                textBox1Tab3.AppendText("Complete export results for the filtered m/z." + newLine);

                if (mZAndIntensityDict.Count > 0)
                {
                    textBox1Tab3.AppendText("Start export MSI results for the filtered m/z." + newLine);

                    List<double> list = new List<double>();
                    foreach (object eachMz in listBoxSignificantMetabolites.Items)
                    {
                        list.Add(double.Parse(eachMz.ToString()));
                    }
                    Parallel.ForEach(list, eachMzDouble =>
                    //foreach (object eachMz in listBoxSignificantMetabolites.Items)
                    {
                        foreach (KeyValuePair<string, ConcurrentDictionary<double, ConcurrentDictionary<int, double[]>>> slicemZAndHeatbin in mZAndIntensityDict)
                        {
                            string sliceName = slicemZAndHeatbin.Key.ToString();

                            string[] eachSlicesDataDirectory = Directory.GetDirectories(ROIexportLocation, "*", SearchOption.TopDirectoryOnly);
                            string eachSlicesDataDirectoryName = ROIexportLocation + @"\Tab3 export MSI data for " + sliceName;

                            if (!eachSlicesDataDirectory.Contains(eachSlicesDataDirectoryName))
                            {
                                _ = Directory.CreateDirectory(eachSlicesDataDirectoryName);
                            }
                            else
                            {
                                //DirectoryInfo di = new(eachSlicesDataDirectoryName);
                                //foreach (FileInfo file in di.EnumerateFiles())
                                //{
                                //    file.Delete();
                                //}
                            }

                            int colMin = colNumDict[sliceName];
                            int rawFilesCount = rawFileNumberDict[sliceName];
                            for (int i = 0; i < colMin; i++)
                            {
                                firstRow = firstRow + "\t" + "intensity" + (i + 1);
                            }

                            if (slicemZAndHeatbin.Value.ContainsKey(eachMzDouble))
                            {
                                string fileName2 = eachSlicesDataDirectoryName + @"\" + eachMzDouble + ".txt";

                                using (StreamWriter f = new(fileName2))
                                {
                                    f.WriteLine(firstRow);

                                    for (int i = 0; i < rawFilesCount; i++)
                                    {
                                        string writeResult = eachMzDouble.ToString();
                                        for (int j = 0; j < colMin; j++)
                                        {
                                            writeResult = writeResult + "\t" + slicemZAndHeatbin.Value[eachMzDouble][i][j];
                                            //writeResult = writeResult + "\t" + slicemZAndHeatbin.Value[eachMz][i][j]; // first is the column number
                                        }
                                        f.WriteLine(writeResult);
                                    }
                                }
                            }
                        }
                    });
                    textBox1Tab3.AppendText("Complete export ROI and filtered m/z results for all slices." + newLine);
                }
                else
                {
                    textBox1Tab3.AppendText("Only ROI and P values were exported, no available MSI intensity data for m/z." + newLine);
                    return;
                }
            }
        }


        /// <summary>
        /// With items in listBoxSignificantMetabolites.Items
        /// Match with self-defined database and export the matching results to the path of ROI data
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void buttonTab3MatchDatabase_Click(object sender, EventArgs e)
        {
            List<string> adductType = new();
            List<int> adductIndex = new();
            int ppmThreshold = 5;

            List<string> compoundName = new List<string>();

            if (checkBoxTab3Pos.Checked | checkBoxTab3Neg.Checked)
            {
                if (checkBoxTab3Pos.Checked)
                {
                    adductType = new List<string> { "COMPOUND", "M+H", "M+Na", "M+K", "M+NH4", "Database", "Database_ID" };
                }

                if (checkBoxTab3Neg.Checked)
                {
                    adductType = new List<string> { "COMPOUND", "M-H", "M+Cl", "M-H2O-H", "Database", "Database_ID" };
                }
            }
            else
            {
                MessageBox.Show("Please choose the ion mode first.");
                return;
            }


            OpenFileDialog openFileDialog = new();
            openFileDialog.Filter = "csv files (*.csv)|*.csv";
            openFileDialog.Title = "Please choose the dictionary";

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                string fileName = openFileDialog.FileName; // the file name of dictionary
                                                           //exportLocation = Path.GetDirectoryName(fileName);

                string exportFileName = "";

                if (ROIexportLocation == "")
                {
                    textBox1Tab3.AppendText("The m/z and P_value was not exported before, so the matching results would be stored in the directory of dictionary." + newLine);
                    ROIexportLocation = Path.GetDirectoryName(fileName);
                    exportFileName = Path.GetDirectoryName(fileName) + @"/results matched with " + Path.GetFileNameWithoutExtension(fileName) + DateTime.Now.ToString("yyyyMMdd_hhmmss") + ".txt";
                }
                else
                {
                    textBox1Tab3.AppendText("The m/z and P_value was exported before, so the matching results would be stored in the same exported directory." + newLine);
                    exportFileName = ROIexportLocation + @"/results matched with " + Path.GetFileNameWithoutExtension(fileName) + DateTime.Now.ToString("yyyy_MM_dd") + ".txt";
                }

                engine.SetSymbol("diction_matching_res_to_save", engine.CreateCharacter(exportFileName.Replace(@"\", @"/")));

                //string exportFileName = ROIexportLocation + @"/results matched with " + Path.GetFileNameWithoutExtension(fileName) + ".txt";

                using (var reader = new StreamReader(fileName))
                using (StreamWriter f = new(exportFileName))
                {
                    f.WriteLine("m/z" + "\t" + "compund" + "\t" + "adduct" + "\t" + "benchmark m/z" + "\t" + "ppm difference" + "\t" + "database" + "\t" + "database_ID");
                    // read the first line in the begining
                    List<string> headerNames = reader.ReadLine().Split(",").ToList();


                    #region first check the headers and return the index
                    if (headerNames.Intersect(adductType).Any())
                    {
                        foreach (string adduct in adductType)
                        {
                            if (headerNames.Contains(adduct))
                            {
                                var index = headerNames
                                    .Select((element, eachIndex) => new { element, eachIndex })
                                    .Where(m => m.element == adduct)
                                    .ToList();

                                if (index.Count() != 1)
                                {
                                    MessageBox.Show("The " + adduct + " column is duplicated in the current dictionary." + newLine);
                                    return;
                                }
                                else
                                {
                                    adductIndex.Add(index[0].eachIndex);
                                }
                            }
                            else
                            {
                                textBox1Tab3.AppendText("The " + adduct + " column is not present in the current dictionary." + newLine);
                            }
                        }
                    }
                    else
                    {
                        MessageBox.Show("None of the provided adduct appears in the current dictionary." + newLine);
                        return;
                    }
                    #endregion

                    var lineNumber = 1;
                    while (!reader.EndOfStream)
                    {
                        //List<string> values = reader.ReadLine().Split(",").ToList();

                        string csvEachLine = reader.ReadLine();
                        List<string> values = new();
                        //TextFieldParser parser = new TextFieldParser(csvEachLine);
                        //parser.HasFieldsEnclosedInQuotes = true;
                        //parser.SetDelimiters(",");

                        //while (!parser.EndOfData)
                        //{
                        //    values = parser.ReadFields().ToList();
                        //}

                        Regex CSVParser = new Regex(",(?=(?:[^\"]*\"[^\"]*\")*(?![^\"]*\"))");
                        values = CSVParser.Split(csvEachLine).ToList();

                        for (int i = 1; i < adductIndex.Count - 2; i++) // the first is the compund the last two is database and database_ID
                        {
                            if (double.TryParse(values[adductIndex[i]], out double eachValue)) // if NA then the TryParse would throw error
                            {
                                if (listBoxSignificantMetabolites.Items.Count > 0)
                                {
                                    foreach (object filtered_mz_string_p in listBoxSignificantMetabolites.Items)
                                    {
                                        string filtered_mz_string = listBoxSignificantMetabolites.GetItemText(filtered_mz_string_p);
                                        double filtered_mz = new();
                                        if (Regex.IsMatch(filtered_mz_string, "-"))
                                        {
                                            filtered_mz = double.Parse(filtered_mz_string.Substring(0, filtered_mz_string.IndexOf("-")).Trim());
                                        }
                                        else
                                        {
                                            filtered_mz = double.Parse(filtered_mz_string);
                                        }
                                        //double filtered_mz = double.Parse(filtered_mz_string);
                                        double actualDiff = Math.Abs((filtered_mz - eachValue) / eachValue) * 1000000;

                                        if (actualDiff < ppmThreshold)
                                        {
                                            f.WriteLine(filtered_mz + "\t" + values[adductIndex.First()] + "\t" + adductType[i] + "\t" + eachValue + "\t" + actualDiff + "\t" + values[adductIndex[adductIndex.Count - 2]] + "\t" + values[adductIndex.Last()]);
                                        }
                                        else
                                        {
                                            continue;
                                        }
                                    }
                                }
                                else
                                {
                                    textBox1Tab3.AppendText("No m/z present in the box.");
                                    return;
                                }
                            }
                            else
                            {
                                textBox1Tab3.AppendText("No m/z as a number present in line " + lineNumber + " and column " + i + ", the content is " + values[adductIndex[i]] + "." + newLine);
                            }
                        }
                        lineNumber++;
                    }
                }
                textBox1Tab3.AppendText("Complete matching dictionary for the filtered m/z." + newLine);

                engine.Evaluate("data =read.delim(diction_matching_res_to_save)\r\n");
                engine.Evaluate("library(ggplot2)\r\nlibrary(ggsankey)\r\nlibrary(dplyr)\r\ncolnames(data)[3] = c(\"Adduct\")\r\ncolnames(data)[6] = c(\"Database\")\r\ndf <- data |> \r\n  make_long(Adduct, Database)\r\n\r\nreagg <- df%>%\r\n  dplyr::group_by(node)%>%  # Here we are grouping the data by node and then we are taking the frequency of it \r\n  tally()\r\n\r\ndf2 <- merge(df, \r\n             reagg, \r\n             by.x = 'node', \r\n             by.y = 'node', \r\n             all.x = TRUE)\r\n\r\npl <- ggplot(df2, aes(x = x,                        \r\n                      next_x = next_x,                                     \r\n                      node = node,\r\n                      next_node = next_node,        \r\n                      fill = factor(node),\r\n                      label = paste0(node, \": \", n)))             # This Creates a label for each node\r\n\r\npl <- pl +geom_sankey(flow.alpha = 0.5,          #This Creates the transparency of your node \r\n                      node.color = \"black\",     # This is your node color        \r\n                      show.legend = TRUE)        # This determines if you want your legend to show\r\n\r\npl <- pl + geom_sankey_label(Size = 1,\r\n                             color = \"black\", \r\n                             fill = \"white\") # This specifies the Label format for each node \r\n\r\n\r\npl <- pl + theme_sankey(base_size = 16) \r\npl <- pl + theme(legend.position = 'none')\r\npl <- pl + theme(axis.title = element_blank(),\r\n                 axis.text.y = element_blank(),\r\n                 axis.ticks = element_blank(),\r\n                 panel.grid = element_blank(),\r\n                 axis.text.x = element_text(size = 15))\r\n\r\n\r\n# pl <- pl + scale_fill_viridis_d(option = \"inferno\")\r\n# pl <- pl + labs(title = \"Creating a Sankey Diagram\")\r\n# pl <- pl + labs(subtitle = \"Using a simplified ficticious data\")\r\n# pl <- pl + labs(caption =\"Opeyemi Omiwale\" )\r\npl <- pl + labs(fill = 'Nodes')");
                engine.Evaluate("ggsave(plot = pl, filename = paste(diction_matching_res_to_save, \"sankeyPlot.pdf\"),width = 4,height = 5,units = \"in\")");
                engine.Evaluate("library(ComplexUpset)\r\ndata_for_ComplexUpset <- data |> select(m.z, Database) |> distinct() |> mutate(present = T) |>  spread(Database, present, fill = F ) \r\ndatabase_names = unique(data$Database)\r\nupset_plot = upset(data_for_ComplexUpset,database_names)\r\nggsave(plot = upset_plot, filename = paste(diction_matching_res_to_save, \"upsetPlot.pdf\"),width = 7,height = 5,units = \"in\")\r\n");

                textBox1Tab3.AppendText("Complete exporting upset and sankey plots." + newLine);
            }
        }

        /// <summary>
        /// Enrichment analysis with msea R package
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void buttonTab3Enrichment_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog = new();
            openFileDialog.Filter = "txt files (*.txt)|*.txt";
            openFileDialog.Title = "Please choose the dictionary matching results";

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                string inputData = openFileDialog.FileName.Replace(@"\", "/");
                engine.SetSymbol("dic_matching_res", engine.CreateCharacter(inputData));
                engine.Evaluate("dic_matching_res = read.delim(dic_matching_res)");

                if (radioButton1MSEA.Checked)
                {
                    OpenFileDialog openFileDialog2 = new();
                    openFileDialog2.Filter = "rds files (*.rds)|*.rds";
                    openFileDialog2.Title = "Please choose the '.rds' data file ";

                    if (openFileDialog2.ShowDialog() == DialogResult.OK)
                    {
                        cbt.Install();
                        string databaseFile = openFileDialog2.FileName.Replace(@"\", "/");
                        engine.SetSymbol("output_KEGG_database", engine.CreateCharacter(databaseFile));
                        engine.Evaluate("load(output_KEGG_database)");

                        engine.Evaluate("KEGG_ID = dic_matching_res[dic_matching_res$database == \"KEGG\",7]");
                        engine.Evaluate("library(dplyr)");
                        //functions from msea packgae
                        engine.Evaluate("msea_new <- function(metabolite.set, queryset, atype = \"E\", midp = FALSE) {\r\n  if (atype == \"ED\") \r\n    alternative = \"two.sided\" \r\n  else if (atype == \"D\") \r\n    alternative = \"less\" \r\n  else alternative = \"greater\"\r\n  if (!is.logical(midp)) \r\n    stop(\"'midp' must be logical\")\r\n  mets.refset <- unique(unlist(lapply(metabolite.set$KEGG_id, function(x) unlist(strsplit(unlist(x), split = \" \")))))\r\n  res.fisher.test <- apply(metabolite.set, 1, calc.fisher.test, \r\n                           queryset, mets.refset, alternative, midp)\r\n  res <- formatting.results(res.fisher.test)\r\n  class(res) <- c(\"msea\", \"data.frame\")\r\n  return(res)\r\n}\r\n\r\n## This function performs Fisher's exact test.\r\ncalc.fisher.test <- function(list, queryset, mets.refset, alternative, midp) {\r\n  res <- list()\r\n  res$ID <- unlist(list[1])\r\n  res$setname <- unlist(list[2])\r\n  metabolite.set <- unlist(strsplit(unlist(list[3]), split = \" \"))\r\n  mets.interest <- intersect(queryset, metabolite.set)\r\n  \r\n  yy <- length(intersect(metabolite.set, queryset))\r\n  yn <- length(setdiff(metabolite.set, queryset))\r\n  ny <- length(queryset) - yy\r\n  nn <- length(mets.refset) + yy - length(queryset) - length(metabolite.set)\r\n  \r\n  if (midp) {\r\n    test.res <- exact2x2::fisher.exact(rbind(c(yy, yn), c(ny, nn)), \r\n                                       alternative = alternative, \r\n                                       midp = midp)\r\n  } else {\r\n    test.res <- stats::fisher.test(rbind(c(yy, yn), c(ny, nn)), \r\n                                   alternative = alternative)\r\n  }\r\n  \r\n  res$pvalue <- test.res$p.value\r\n  \r\n  res$total <- length(metabolite.set)\r\n  res$hit <- yy\r\n  \r\n  expected <- length(queryset) * (length(metabolite.set)/length(mets.refset))\r\n  res$expected <- round(expected, digits = 2)\r\n  \r\n  res$overlap.percent <- \r\n    round(length(mets.interest)/length(metabolite.set) * 100, digits = 2)\r\n  res$overlap.metabolites <- paste(mets.interest, collapse = \", \")\r\n  \r\n  ## Correcting multiple testing problem by BH method\r\n  res$pvalue <- format(res$pvalue, scientific = F, digits = 4)\r\n  \r\n  return(res)\r\n}\r\n\r\n## This function formats the results of MSEA.\r\nformatting.results <- function(list) {\r\n  res <- data.frame()\r\n  pathway.ID <- unlist(lapply(list, function(x) unlist(x$ID)))\r\n  Metaboliteset.name <- unlist(lapply(list, function(x) unlist(x$setname)))\r\n  Total <- unlist(lapply(list, function(x) unlist(x$total)))\r\n  Expected <- unlist(lapply(list, function(x) unlist(x$expected)))\r\n  Hit <- unlist(lapply(list, function(x) unlist(x$hit)))\r\n  p.value <- unlist(lapply(list, function(x) unlist(x$pvalue)))\r\n  Holm.p <- stats::p.adjust(unlist(lapply(list, \r\n                                          function(x) unlist(x$pvalue))), \r\n                            method = \"holm\")\r\n  FDR <- stats::p.adjust(unlist(lapply(list, \r\n                                       function(x) unlist(x$pvalue))), \r\n                         method = \"fdr\")\r\n  Overlap.percent <- unlist(lapply(list, \r\n                                   function(x) unlist(x$overlap.percent)))\r\n  Overlap.metabolites <- unlist(\r\n    lapply(list, function(x) unlist(x$overlap.metabolites)))\r\n  ## URL <- paste('http://www.genome.jp/dbget-bin/www_bget?', \r\n  ## KEGG.pathway.ID, sep = '')\r\n  res <- data.frame(cbind(pathway.ID, Metaboliteset.name, Total, Expected, \r\n                          Hit, p.value, Holm.p, FDR, Overlap.percent, \r\n                          Overlap.metabolites))\r\n  ordered.res <- res[order(as.numeric(as.character(res$p.value))), ]\r\n  return(ordered.res)\r\n}\r\n");
                        engine.Evaluate("## Overview plot of MSEA results This function plots the results of MSEA.  \r\n## It was originally from the MetaboAnalyst.  The code was modified under \r\n## the licence GPL-2.  plot <- function(object){ UseMethod('plot') }\r\n\r\n# print <- function(object){ UseMethod('print') } ##' @export print.msea <-\r\n# function(object) { object <- data.frame(object) print.data.frame(object) }\r\n\r\n##' @export\r\nplot.msea <- function(x, col = \"cm.colors\", show.limit = 20, ...) {\r\n  if (!methods::is(x, \"msea\")) {\r\n    stop(\"msea.res should be msea object.\")\r\n  }\r\n  \r\n  barplot(x)\r\n}\r\n\r\n#' plot count bar on each meatbolite-set\r\n#'\r\n#' @param x a msea object\r\n#' @param col color scheme\r\n#' @param show.limit the number of metabolite-sets to plot\r\n#' @return plot\r\n#' @examples\r\n#' library(MSEApdata)\r\n#' data(mset_SMPDB_Metabolic_format_HMDB)\r\n#' data(msea.example)\r\n#' res <- msea(mset_SMPDB_Metabolic_format_HMDB, msea.example)\r\n#' barplot(res)\r\n##' @export\r\nbarplot <- function(x, col = \"cm.colors\", show.limit = 20) {\r\n  if (!methods::is(x, \"msea\")) {\r\n    stop(\"MSEA result should be msea object.\")\r\n  }\r\n  folds <- as.numeric(as.character(x$Expected))\r\n  counts <- as.numeric(as.character(x$Hit))\r\n  names(counts) <- as.character(x$Metaboliteset.name)\r\n  pvals <- as.numeric(as.character(x$p.val))\r\n  \r\n  # due to space limitation, plot top 50 if more than 50 were given\r\n  title <- \"Metabolite Sets Enrichment Overview\"\r\n  if (length(folds) > show.limit) {\r\n    # folds <- folds[1:show.limit] pvals <- pvals[1:show.limit] counts <-\r\n    # counts[1:show.limit]\r\n    folds <- folds[seq_len(show.limit)]\r\n    pvals <- pvals[seq_len(show.limit)]\r\n    counts <- counts[seq_len(show.limit)]\r\n    title <- paste(\"Enrichment Overview (top \", show.limit, \")\")\r\n  }\r\n  \r\n  op <- graphics::par(mar = c(5, 20, 4, 6), oma = c(0, 0, 0, 4))\r\n  \r\n  if (col == \"cm.colors\") {\r\n    col <- grDevices::cm.colors(length(pvals))\r\n  } else if (col == \"heat.colors\") {\r\n    col <- rev(grDevices::heat.colors(length(pvals)))\r\n  }\r\n  graphics::barplot(rev(counts), horiz = TRUE, \r\n                    col = col, xlab = \"Count\", las = 1, \r\n                    # cex.name = 0.75,\r\n                    space = c(0.5, 0.5), main = title)\r\n  \r\n  \r\n  minP <- min(pvals)\r\n  maxP <- max(pvals)\r\n  medP <- (minP + maxP)/2\r\n  \r\n  axs.args <- list(at = c(minP, medP, maxP), \r\n                   labels = format(c(maxP, medP, minP), \r\n                                   scientific = TRUE, digit = 1), \r\n                   tick = FALSE)\r\n  image.plot(legend.only = TRUE, zlim = c(minP, maxP), \r\n             col = col, axis.args = axs.args, \r\n             legend.shrink = 0.4, legend.lab = \"P-value\")\r\n  graphics::par(op)\r\n}\r\n\r\n\r\n#' plot count-sized dot on (x, y) = (metabolite-sets, pvalue)\r\n#'\r\n#' @param x A msea object\r\n#' @param FDR false dicovery rate (default TRUE)\r\n#' @param show.limit the number of metabolite-sets to plot\r\n#' @return plot\r\n#' @examples\r\n#' library(MSEApdata)\r\n#' data(mset_SMPDB_Metabolic_format_HMDB)\r\n#' data(msea.example)\r\n#' res <- msea(mset_SMPDB_Metabolic_format_HMDB, msea.example)\r\n#' dotplot(res)\r\n##' @export\r\ndotplot <- function(x, FDR = TRUE, show.limit = 20) {\r\n  msea.limit <- x[seq_len(show.limit), ]\r\n  indices <- seq_len(show.limit)\r\n  \r\n  gp <- ggplot2::ggplot(\r\n    msea.limit, \r\n    ggplot2::aes(x = msea.limit$Expected, \r\n                 y = stats::reorder(\r\n                   msea.limit$Metaboliteset.name, -indices)))\r\n  \r\n  if (FDR) {\r\n    gp <- gp + ggplot2::geom_point(ggplot2::aes(colour = as.numeric(\r\n      as.character(msea.limit$FDR)),\r\n      size = as.numeric(as.character(msea.limit$Hit)))) + \r\n      ggplot2::labs(colour = \"FDR\", size = \"Hit [count]\")  \r\n  } else {\r\n    gp <- gp + ggplot2::geom_point(ggplot2::aes(colour = as.numeric(\r\n      as.character(msea.limit$p.value)),\r\n      size = as.numeric(as.character(msea.limit$Hit)))) + \r\n      ggplot2::labs(colour = \"P-value\", size = \"Hit [count]\")\r\n  }\r\n  gp <- gp + ggplot2::xlab(\"Expected\") + ggplot2::ylab(\"Metabolite sets\")\r\n  gp\r\n}\r\n\r\n\r\n\r\n\r\n#' plot msea result with network\r\n#' \r\n#' @importFrom magrittr %>%\r\n#' @importFrom grDevices colorRamp rgb\r\n#' @importFrom utils read.csv write.csv\r\n#' @keywords internal\r\n#' @param x A msea result\r\n#' @param mset A list of metabolite-sets\r\n#' @param shared.metabolite The number of shared metabolites\r\n#' @param show.limit The number of metabolite-sets to plot\r\n#' @param sendto The target of the network visualization\r\n#' @return plot\r\n#' @examples \r\n#' library(MSEApdata)\r\n#' data(kusano)\r\n#' data(mset_SMPDB_format_KEGG)\r\n#' res <- msea(mset_SMPDB_format_KEGG, kusano)\r\n#' netplot(res, mset_SMPDB_format_KEGG, shared.metabolite = 20)\r\n#' # You can also send the network to Cytoscape with RCy3\r\n#' # netplot(res, mset_SMPDB_format_KEGG, \r\n#' # shared.metabolite = 20, sendto = 'cy')\r\n#' @export\r\nnetplot <- function(x, mset, shared.metabolite = 3, show.limit = 20, \r\n                    sendto = c(\"visnetwork\", \"cytoscape\")) {\r\n  msea <- x[seq_len(show.limit), ]\r\n  pathwayIds <- msea$pathway.ID\r\n  pvals <- as.numeric(as.character(msea$p.value))\r\n  pvalmax <- max(pvals)\r\n  cols <- colorRamp(c(\"red\", \"gray\"))(pvals/pvalmax)\r\n  \r\n  torgb <- function(y) {\r\n    y <- as.integer(y)\r\n    return(rgb(y[1], y[2], y[3], maxColorValue = 255))\r\n  }\r\n  \r\n  nodecols <- apply(cols, 1, torgb)\r\n  \r\n  msea %>% \r\n    dplyr::rename(id = \"pathway.ID\", \r\n                  label = \"Metaboliteset.name\", value = \"Hit\") %>% \r\n    dplyr::mutate(color = nodecols) -> msea\r\n  htmltables <- apply(msea, 1, knitr::kable, format = \"html\")\r\n  # print(head(msea))\r\n  msea4cy <- msea\r\n  msea$title <- htmltables\r\n  \r\n  edges <- write.network(mset, shared.metabolite)\r\n  edges %>% dplyr::filter(from %in% pathwayIds) %>% \r\n    dplyr::filter(to %in% pathwayIds) -> edges\r\n  # print(head(edges))\r\n  \r\n  sendto <- match.arg(sendto)\r\n  if (sendto == \"visnetwork\") {\r\n    visNetwork::visNetwork(msea, edges)\r\n  } else if (sendto == \"cytoscape\") {\r\n    ig <- igraph::graph.data.frame(edges, directed = FALSE, \r\n                                   vertices = msea4cy)\r\n    # igraph::write_graph(g, file = paste(deparse(substitute(mset)),\r\n    # 'graphml', sep = '.'), format = 'graphml')\r\n    RCy3::createNetworkFromIgraph(ig)\r\n  }\r\n  \r\n}\r\n\r\nwrite.network <- function(mset, shared.metabolite = 3) {\r\n  i_j <- arrangements::combinations(length(mset), 2)\r\n  edges <- t(apply(i_j, 1, function(x, s){\r\n    i <- x[1]\r\n    j <- x[2]\r\n    fromCpds <- mset[[i]][[3]]\r\n    fromId <- mset[[i]][[1]]\r\n    toCpds <- mset[[j]][[3]]\r\n    sharedCpds <- intersect(fromCpds, toCpds)\r\n    if (length(sharedCpds) >= s) {\r\n      toId <- mset[[j]][[1]]\r\n      shared <- paste(sort(sharedCpds), collapse = \" \")\r\n      return(c(from=fromId, to=toId, shared=shared))\r\n    }else{\r\n      return(c(from=NA, to=NA, shared=NA))\r\n    }\r\n  }, s=shared.metabolite))\r\n  edges <- as.data.frame(edges[which(!is.na(edges[,1])), ])\r\n}\r\n\r\n## image.plot Plot strip of color key by figure side Adapted from the \r\n## image.plot in fields package to correct label so that small p value is \r\n## bigger, located in top of the color key\r\nimage.plot <- function(..., add = FALSE, nlevel = 64, horizontal = FALSE, \r\n                       legend.shrink = 0.9, legend.width = 1.2, \r\n                       legend.mar = ifelse(horizontal, 3.1, 5.1), \r\n                       legend.lab = NULL, graphics.reset = FALSE, \r\n                       bigplot = NULL, smallplot = NULL, legend.only = FALSE, \r\n                       col = fields::tim.colors(nlevel), lab.breaks = NULL, \r\n                       axis.args = NULL, legend.args = NULL, midpoint = FALSE) \r\n{\r\n  \r\n  old.par <- graphics::par(no.readonly = TRUE)\r\n  # figure out zlim from passed arguments\r\n  info <- image.plot.info(...)\r\n  if (add) {\r\n    big.plot <- old.par$plt\r\n  }\r\n  if (legend.only) {\r\n    graphics.reset <- TRUE\r\n  }\r\n  if (is.null(legend.mar)) {\r\n    legend.mar <- ifelse(horizontal, 3.1, 5.1)\r\n  }\r\n  # figure out how to divide up the plotting real estate.\r\n  temp <- image.plot.plt(add = add, legend.shrink = legend.shrink, \r\n                         legend.width = legend.width, \r\n                         legend.mar = legend.mar, \r\n                         horizontal = horizontal, bigplot = bigplot, \r\n                         smallplot = smallplot)\r\n  # bigplot are plotting region coordinates for image smallplot are plotting\r\n  # coordinates for legend\r\n  smallplot <- temp$smallplot\r\n  bigplot <- temp$bigplot\r\n  # draw the image in bigplot, just call the R base function or poly.image for\r\n  # polygonal cells note logical switch for poly.grid parsed out of call from\r\n  # image.plot.info\r\n  if (!legend.only) {\r\n    if (!add) {\r\n      graphics::par(plt = bigplot)\r\n    }\r\n    if (!info$poly.grid) {\r\n      graphics::image(..., add = add, col = col)\r\n    } else {\r\n      fields::poly.image(..., add = add, col = col, midpoint = midpoint)\r\n    }\r\n    big.par <- graphics::par(no.readonly = TRUE)\r\n  }\r\n  ## check dimensions of smallplot\r\n  if ((smallplot[2] < smallplot[1]) | (smallplot[4] < smallplot[3])) {\r\n    graphics::par(old.par)\r\n    stop(\"plot region too small to add legend\\n\")\r\n  }\r\n  # Following code draws the legend using the image function and a one column\r\n  # image.  calculate locations for colors on legend strip\r\n  ix <- 1\r\n  minz <- info$zlim[1]\r\n  maxz <- info$zlim[2]\r\n  binwidth <- (maxz - minz)/nlevel\r\n  midpoints <- seq(minz + binwidth/2, maxz - binwidth/2, by = binwidth)\r\n  iy <- midpoints\r\n  iz <- matrix(iy, nrow = 1, ncol = length(iy))\r\n  # extract the breaks from the ... arguments note the breaks delineate \r\n  # intervals of common color\r\n  breaks <- list(...)$breaks\r\n  # draw either horizontal or vertical legends. using either suggested breaks \r\n  # or not -- a total of four cases.  next par call sets up a new plotting \r\n  # region just for the legend strip at the smallplot coordinates\r\n  graphics::par(new = TRUE, pty = \"m\", plt = smallplot, err = -1)\r\n  # create the argument list to draw the axis this avoids 4 separate calls \r\n  # to axis and allows passing extra arguments.  then add axis with specified \r\n  # lab.breaks at specified breaks\r\n  if (!is.null(breaks) & !is.null(lab.breaks)) {\r\n    # axis with labels at break points\r\n    axis.args <- c(list(side = ifelse(horizontal, 1, 4), mgp = c(3, 1, 0), \r\n                        las = ifelse(horizontal, 0, 2), at = breaks, \r\n                        labels = lab.breaks), axis.args)\r\n  } else {\r\n    # If lab.breaks is not specified, with or without breaks, pretty tick \r\n    # mark locations and labels are computed internally, or as specified in \r\n    # axis.args at the function call\r\n    axis.args <- c(list(side = ifelse(horizontal, 1, 4), mgp = c(3, 1, 0), \r\n                        las = ifelse(horizontal, 0, 2)), axis.args)\r\n  }\r\n  # draw color scales the four cases are horizontal/vertical breaks/no breaks \r\n  # add a label if this is passed.\r\n  if (!horizontal) {\r\n    if (is.null(breaks)) {\r\n      graphics::image(ix, iy, iz, xaxt = \"n\", yaxt = \"n\", \r\n                      xlab = \"\", ylab = \"\", col = col)\r\n    } else {\r\n      graphics::image(ix, iy, iz, xaxt = \"n\", yaxt = \"n\", \r\n                      xlab = \"\", ylab = \"\", col = col, breaks = breaks)\r\n    }\r\n  } else {\r\n    if (is.null(breaks)) {\r\n      graphics::image(iy, ix, t(iz), xaxt = \"n\", yaxt = \"n\", \r\n                      xlab = \"\", ylab = \"\", col = col)\r\n    } else {\r\n      graphics::image(iy, ix, t(iz), xaxt = \"n\", yaxt = \"n\", \r\n                      xlab = \"\", ylab = \"\", col = col, breaks = breaks)\r\n    }\r\n  }\r\n  \r\n  # now add the axis to the legend strip. notice how all the information is \r\n  # in the list axis.args\r\n  do.call(\"axis\", axis.args)\r\n  \r\n  # add a box around legend strip\r\n  graphics::box()\r\n  \r\n  # add a label to the axis if information has been supplied using the mtext\r\n  # function. The arguments to mtext are passed as a list like the drill for \r\n  # axis (see above)\r\n  if (!is.null(legend.lab)) {\r\n    legend.args <- list(text = legend.lab, \r\n                        side = ifelse(horizontal, 1, 3), line = 1)\r\n  }\r\n  # add the label using mtext function\r\n  if (!is.null(legend.args)) {\r\n    do.call(graphics::mtext, legend.args)\r\n  }\r\n  # clean up graphics device settings reset to larger plot region with right \r\n  # user coordinates.\r\n  mfg.save <- graphics::par()$mfg\r\n  if (graphics.reset | add) {\r\n    graphics::par(old.par)\r\n    graphics::par(mfg = mfg.save, new = FALSE)\r\n    invisible()\r\n  } else {\r\n    graphics::par(big.par)\r\n    graphics::par(plt = big.par$plt, xpd = FALSE)\r\n    graphics::par(mfg = mfg.save, new = FALSE)\r\n    invisible()\r\n  }\r\n}\r\n\r\n## image.plot.info\r\n\"image.plot.info\" <- function(...) {\r\n  temp <- list(...)\r\n  # \r\n  xlim <- NA\r\n  ylim <- NA\r\n  zlim <- NA\r\n  poly.grid <- FALSE\r\n  # go through various cases of what these can be x,y,z list is first argument\r\n  if (is.list(temp[[1]])) {\r\n    xlim <- range(temp[[1]]$x, na.rm = TRUE)\r\n    ylim <- range(temp[[1]]$y, na.rm = TRUE)\r\n    zlim <- range(temp[[1]]$z, na.rm = TRUE)\r\n    if (is.matrix(temp[[1]]$x) & is.matrix(temp[[1]]$y) & \r\n        is.matrix(temp[[1]]$z)) {\r\n      poly.grid <- TRUE\r\n    }\r\n  }\r\n  ##### check for polygrid first three arguments should be matrices\r\n  if (length(temp) >= 3) {\r\n    if (is.matrix(temp[[1]]) & is.matrix(temp[[2]]) & \r\n        is.matrix(temp[[3]])) {\r\n      poly.grid <- TRUE\r\n    }\r\n  }\r\n  ##### z is passed without an x and y (and not a poly.grid!)\r\n  if (is.matrix(temp[[1]]) & !poly.grid) {\r\n    xlim <- c(0, 1)\r\n    ylim <- c(0, 1)\r\n    zlim <- range(temp[[1]], na.rm = TRUE)\r\n  }\r\n  #### if x,y,z have all been passed find their ranges. \r\n  #### holds if poly.grid or not\r\n  if (length(temp) >= 3) {\r\n    if (is.matrix(temp[[3]])) {\r\n      xlim <- range(temp[[1]], na.rm = TRUE)\r\n      ylim <- range(temp[[2]], na.rm = TRUE)\r\n      zlim <- range(temp[[3]], na.rm = TRUE)\r\n    }\r\n  }\r\n  #### parse x,y,z if they are named arguments determine if this is polygon \r\n  #### grid (x and y are matrices)\r\n  if (is.matrix(temp$x) & is.matrix(temp$y) & is.matrix(temp$z)) {\r\n    poly.grid <- TRUE\r\n  }\r\n  xthere <- match(\"x\", names(temp))\r\n  ythere <- match(\"y\", names(temp))\r\n  zthere <- match(\"z\", names(temp))\r\n  if (!is.na(zthere)) \r\n    zlim <- range(temp$z, na.rm = TRUE)\r\n  if (!is.na(xthere)) \r\n    xlim <- range(temp$x, na.rm = TRUE)\r\n  if (!is.na(ythere)) \r\n    ylim <- range(temp$y, na.rm = TRUE)\r\n  # overwrite zlims with passed values\r\n  if (!is.null(temp$zlim)) \r\n    zlim <- temp$zlim\r\n  if (!is.null(temp$xlim)) \r\n    xlim <- temp$xlim\r\n  if (!is.null(temp$ylim)) \r\n    ylim <- temp$ylim\r\n  list(xlim = xlim, ylim = ylim, zlim = zlim, poly.grid = poly.grid)\r\n}\r\n\r\n\r\n## image.plot.plt Copyright 2004-2007, Institute for Mathematics Applied\r\n## Geosciences University Corporation for Atmospheric Research Licensed \r\n## under the GPL -- www.gpl.org/licenses/gpl.html\r\nimage.plot.plt <- function(x, add = FALSE, legend.shrink = 0.9, \r\n                           legend.width = 1, horizontal = FALSE, \r\n                           legend.mar = NULL, bigplot = NULL, \r\n                           smallplot = NULL, ...) {\r\n  old.par <- graphics::par(no.readonly = TRUE)\r\n  if (is.null(smallplot)) \r\n    stick <- TRUE else stick <- FALSE\r\n    if (is.null(legend.mar)) {\r\n      legend.mar <- ifelse(horizontal, 3.1, 5.1)\r\n    }\r\n    # compute how big a text character is\r\n    char.size <- ifelse(horizontal, \r\n                        graphics::par()$cin[2]/graphics::par()$din[2], \r\n                        graphics::par()$cin[1]/graphics::par()$din[1])\r\n    # This is how much space to work with based on setting the margins in the \r\n    # high level par command to leave between strip and big plot\r\n    offset <- char.size * ifelse(horizontal, \r\n                                 graphics::par()$mar[1], graphics::par()$mar[4])\r\n    # this is the width of the legned strip itself.\r\n    legend.width <- char.size * legend.width\r\n    # this is room for legend axis labels\r\n    legend.mar <- legend.mar * char.size\r\n    # smallplot is the plotting region for the legend.\r\n    if (is.null(smallplot)) {\r\n      smallplot <- old.par$plt\r\n      if (horizontal) {\r\n        smallplot[3] <- legend.mar\r\n        smallplot[4] <- legend.width + smallplot[3]\r\n        pr <- (smallplot[2] - smallplot[1]) * ((1 - legend.shrink)/2)\r\n        smallplot[1] <- smallplot[1] + pr\r\n        smallplot[2] <- smallplot[2] - pr\r\n      } else {\r\n        smallplot[2] <- 1 - legend.mar\r\n        smallplot[1] <- smallplot[2] - legend.width\r\n        pr <- (smallplot[4] - smallplot[3]) * ((1 - legend.shrink)/2)\r\n        smallplot[4] <- smallplot[4] - pr\r\n        smallplot[3] <- smallplot[3] + pr\r\n      }\r\n    }\r\n    if (is.null(bigplot)) {\r\n      bigplot <- old.par$plt\r\n      if (!horizontal) {\r\n        bigplot[2] <- min(bigplot[2], smallplot[1] - offset)\r\n      } else {\r\n        bottom.space <- old.par$mar[1] * char.size\r\n        bigplot[3] <- smallplot[4] + offset\r\n      }\r\n    }\r\n    if (stick & (!horizontal)) {\r\n      dp <- smallplot[2] - smallplot[1]\r\n      smallplot[1] <- min(bigplot[2] + offset, smallplot[1])\r\n      smallplot[2] <- smallplot[1] + dp\r\n    }\r\n    return(list(smallplot = smallplot, bigplot = bigplot))\r\n}");
                        //plot functions
                        engine.Evaluate("msea_result = msea_new(output_KEGG_database, KEGG_ID)\r\nmsea_result$`Enrichment score` = as.numeric(msea_result$Hit) / as.numeric(msea_result$Expected)");
                        engine.Evaluate("msea_result2 = msea_result |> arrange(as.numeric(p.value), desc(as.numeric (`Enrichment score`)))");
                        engine.Evaluate("barplot2_siwei <- function(x, col = \"cm.colors\", show.limit = 20) {\r\n  if (!methods::is(x, \"msea\")) {\r\n    stop(\"MSEA result should be msea object.\")\r\n  }\r\n  folds <- as.numeric(as.character(x$Expected))\r\n  counts <- as.numeric(as.character(x$`Enrichment score`))\r\n  names(counts) <- as.character(x$Metaboliteset.name)\r\n  pvals <- as.numeric(x$p.val)\r\n  \r\n  # due to space limitation, plot top 50 if more than 50 were given\r\n  title <- \"Metabolite Sets Enrichment Overview\"\r\n  if (length(folds) > show.limit) {\r\n    # folds <- folds[1:show.limit] pvals <- pvals[1:show.limit] counts <-\r\n    # counts[1:show.limit]\r\n    folds <- folds[seq_len(show.limit)]\r\n    pvals <- pvals[seq_len(show.limit)]\r\n    counts <- counts[seq_len(show.limit)]\r\n    title <- paste(\"Enrichment Overview (top \", show.limit, \")\")\r\n  }\r\n  \r\n  op <- graphics::par(mar = c(5, 20, 4, 6), oma = c(0, 0, 0, 4))\r\n  \r\n  if (col == \"cm.colors\") {\r\n    col <- grDevices::cm.colors(length(pvals))\r\n  } else if (col == \"heat.colors\") {\r\n    col <- rev(grDevices::heat.colors(length(pvals)))\r\n  }\r\n  graphics::barplot(rev(counts), horiz = TRUE, \r\n                    col = col, xlab = \"Enrichment score\", las = 1, \r\n                    # cex.name = 0.75,\r\n                    space = c(0.5, 0.5), main = title)\r\n  \r\n  \r\n  minP <- min(pvals)\r\n  maxP <- max(pvals)\r\n  medP <- (minP + maxP)/2\r\n  \r\n  axs.args <- list(at = c(minP, medP, maxP), \r\n                   labels = format(c(maxP, medP, minP), \r\n                                   scientific = TRUE, digit = 3), \r\n                   tick = FALSE)\r\n  image.plot(legend.only = TRUE, zlim = c(minP, maxP), \r\n             col = col, axis.args = axs.args, \r\n             legend.shrink = 0.4, legend.lab = \"P-value\")\r\n  graphics::par(op)\r\n}");
                        engine.Evaluate("barplot2_siwei(msea_result2, col = \"heat.colors\", show.limit = 10)");
                        cbt.Uninstall();
                    }
                }
                else
                {
                    if (radioButton2Network.Checked)
                    {

                        //LogicalVector dataexist = engine.Evaluate("exists('dic_matching_res')").AsLogical();
                        //if (!dataexist[0])
                        //{
                        //    MessageBox.Show("Please do the MSEA enrichment analysis first.");
                        //    return;
                        //}


                        //plot FELLA
                        using var FELLA = new FolderBrowserDialog();
                        FELLA.Description = "Select the FELLA database folder";
                        FELLA.UseDescriptionForTitle = true;
                        DialogResult folder = FELLA.ShowDialog();
                        if (folder == DialogResult.OK && !string.IsNullOrWhiteSpace(FELLA.SelectedPath))
                        {
                            string databaseFile = FELLA.SelectedPath.Replace(@"\", "/");
                            engine.SetSymbol("database_directory", engine.CreateCharacter(databaseFile));

                            if (Directory.GetFiles(FELLA.SelectedPath).Length == 0)
                            {
                                textBox1Tab3.AppendText("There is no files in the given directroy, please use the FELLA directory." + newLine);
                                return;
                            }
                            else
                            {
                                //OpenFileDialog openFileDialog2 = new();
                                //openFileDialog2.Filter = "csv files (*.csv)|*.csv";
                                //openFileDialog2.Title = "Please choose the differentially analysis results";
                                //if (openFileDialog2.ShowDialog() == DialogResult.OK)
                                //{
                                //    string filtered_mz = openFileDialog2.FileName.Replace(@"\", "/");
                                //    engine.SetSymbol("filtered_mz_list_file_name", engine.CreateCharacter(filtered_mz));
                                //    engine.Evaluate("filtered_mz_list = read.csv(filtered_mz_list_file_name,header = F)");
                                //    engine.Evaluate("library(tidyverse) \r\nlibrary(dplyr)\r\n rownames(filtered_mz_list) = filtered_mz_list[,1]\r\nfiltered_mz_list = filtered_mz_list[,-1]\r\nrownames(filtered_mz_list)[1] = \"m.z\"\r\n filtered_mz_list = data.frame(t(filtered_mz_list))\r\nfiltered_mzs = dic_matching_res %>% left_join(filtered_mz_list, by = \"m.z\") %>% arrange(P_value) %>% filter(database %in% \"KEGG\") %>% select(database_ID) %>% unique()");

                                //    cbt.Install();
                                //    engine.Evaluate("library(FELLA)\r\nset.seed(1)\r\nfella.data <- loadKEGGdata(\r\n  databaseDir = database_directory,\r\n  internalDir = FALSE,\r\n  loadMatrix = \"diffusion\")\r\n\r\ndataforFELLA <- defineCompounds(\r\n  compounds = filtered_mzs[1:150,],\r\n  data = fella.data)\r\ndataforFELLA <- runDiffusion(\r\n  object = dataforFELLA,\r\n  data = fella.data,\r\n  approx = \"normality\")\r\nnlimit <- 150\r\nvertex.label.cex <- .5\r\nplot(\r\n  dataforFELLA,\r\n  method = \"diffusion\",\r\n  data = fella.data,\r\n  nlimit = nlimit,\r\n  vertex.label.cex = vertex.label.cex)");
                                //    cbt.Uninstall();
                                //}


                                cbt.Install();
                                engine.Evaluate("library(FELLA)\r\nset.seed(1)\r\nfella.data <- loadKEGGdata(\r\n  databaseDir = database_directory,\r\n  internalDir = FALSE,\r\n  loadMatrix = \"diffusion\")\r\n\r\ndataforFELLA <- defineCompounds(\r\n  compounds = dic_matching_res$database_ID,\r\n  data = fella.data)\r\ndataforFELLA <- runDiffusion(\r\n  object = dataforFELLA,\r\n  data = fella.data,\r\n  approx = \"normality\")\r\nnlimit <- 150\r\nvertex.label.cex <- .5\r\nplot(\r\n  dataforFELLA,\r\n  method = \"diffusion\",\r\n  data = fella.data,\r\n  nlimit = nlimit,\r\n  vertex.label.cex = vertex.label.cex)");
                                cbt.Uninstall();
                            }
                        }
                    }
                    else
                    {
                        MessageBox.Show("Please choose the enrichment mode.");
                        return;
                    }
                }
            }
        }
    }
}