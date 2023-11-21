# Multi-MSIProcessor
![MSI TOC for github](https://github.com/biobibibi/MultiMSIProcessor/assets/53837584/6cf7116a-15dc-444f-870c-3e95ac7320f9)

## 1. Briefing

Welcome to the Mult-MSIProcessor page!

As mass spectrometry imaging (MSI) has emerged as a revolutionary method for biomedical research, various MSI data processing applications have emerged but mining the underlying biological mechanisms remains a great challenge. Mult-MSIProcessor is an open-source and freely available C#-based program that could 
* obtain the intensities of m/z in multiple MSI experiments without any format conversion and visualize the MSI data after finishing the data read-in process
* m/z filtering:
  * intensities are mostly missing (> 90%)
  * the average intensity in the background is larger than that in the tissue area
* By selecting the ROI, users could readily group the intensities within the ROI and proceed to statistical and downstream analysis.

## 2. Users Guide
  * Prerequisite:<br />
    * Download Visual Studio https://visualstudio.microsoft.com/downloads/. The community version works just fine.
    * Download the whole project and open the "./MultiMSIProcessor-master/MultiMSIProcessor.sln". Or users can git clone the whole project in Visual Studio-Git-Clone Repository using the following URL: "https://github.com/biobibibi/MultiMSIProcessor".
    * In the Solution Explorer of the Visual Studio, right-click the Project, and find the Add-Project Reference... as shown in the following picture:
    * <img src="https://github-production-user-asset-6210df.s3.amazonaws.com/53837584/281221439-6976c944-d999-40ab-8219-640bc803b65b.png" width= 65%>
    * In the browse tab, click Browse and make sure to add the Supplementary_dll folder to the project. The Supplementary_dll folder is stored in the "./MultiMSIProcessor-master/Supplementary files/dlls/" folder.
    * Then, open the Visual Studio, and hit ctrl+b, if the console says "Build: 1 succeeded, 0 failed, 0 up-to-date, 0 skipped", then you are ready to go.
    * Download R https://www.r-project.org/
    * Run the "Package installation" code stored in the "./MultiMSIProcessor-master/Supplementary files" folder in the project in the R environment.

  * Tab1:<br />
    * For the "Select folder" button: Please choose the folder that contains the subfolders that contain only one of these three formats data: .raw data / .mzXML/ .mzML ().<br />
    "Folder -> Subfolders 1 -> brain1.raw; brain2.raw; brain3.raw... "<br />
    "Folder -> Subfolders 2 -> kidney1.raw; kidney1.raw; kidney1.raw... "<br />
    Choose the "Folder" as input, then click the start extraction<br />
    We provided two demo data in ".raw" format in the "./MultiMSIProcessor-master/Supplementary files/Demo Raw Data/" containing kidney and brain MSI data from rats, which were used as the input files as shown in the tutorial video: "./MultiMSIProcessor-master/Supplementary files/Tab1.mp4".
    * If you want to export the un-filtered data directly without playing around in Tab2 and Tab3, feel free to click "Export all the data" which will help users designate the folder to export. The export files will look like
     "./MSI experiments 1/mz1.txt", "./MSI experiments 1/mz2.txt", "./MSI experiments 1/mz3.txt" ...
     "./MSI experiments 2/mz1.txt", "./MSI experiments 2/mz2.txt", "./MSI experiments 2/mz3.txt" ...
    * Tutorial video for Tab 1 is supplied in the "./MultiMSIProcessor-master/Supplementary files/Tab1.mp4" (till 1:00).
    * When it shows "Please switch to Tab2: Tissue selection for further analyzing", you are good to go.<br />
    ![pic3](https://github.com/biobibibi/MultiMSIProcessor/assets/53837584/8013e7a8-dc60-4a19-9df3-56c9766e586f)

  * Tab2:<br />
    * The QC plot function is recommended to be executed first:
      * 1.Choose the MSI names and the representative mz.
      * 2.For the ROS selection function, users should first check the "Select Rectangle ROI" and choose one selection mode. Then go to the picture to select the background ROIs.
      * 3.After selecting the ROIs, one can type in the integer into the "Number of groups:" and hit "Enter" to add the groups into the group box.
      * 4.After clicking "Matching ROI with intensities" and "Export m/z data within ROI and group info", users would export the QC data into the local computer, which should be the input file for the "QC plot" to generate the QC plot.
      * 5.The QC plot function is shown in the "./MultiMSIProcessor-master/Supplementary files/Tab1.mp4" (from 1:00 to the end).
    * Filtering based on "within tissue area" and "outside tissue area" is highly recommended to shorten the processing time. Users could click the mouse to select and right-click to undo the selection. <br />
      * The filtering tutorial is shown in the "./MultiMSIProcessor-master/Supplementary files/Tab2.mp4" (till 0:30).
    * After filtering, we recommend clicking the "Export Raw data" button to save all datacube into the local, which could be read directly later by "Upload the m/z intensity txt files". The folder-picking and export logic is the same as Tab1.<br />
      * The filtering tutorial is shown in the "./MultiMSIProcessor-master/Supplementary files/Tab2.mp4" (from 0:30 to 0:56).<br />
      ![pic4](https://github.com/biobibibi/MultiMSIProcessor/assets/53837584/f40ff6d2-5095-44e1-9c40-06a76e99f9f7)

    * There are two image export options. "Export the current image" export the currently displayed single image. "Export all images for the selected mz" export the m/z in all MSI experiments. 
    * The threshold allows users to define the maximum intensity of the image. Any pixels with intensity above this threshold would be red after typing in the number in the box and hitting "Enter".
    * Using the same logic as selecting ROIs for the QC plot, users can generate the ROIs in the tissue area and export their intensities.
      * The ROI selection and export function is supplied in the "./MultiMSIProcessor-master/Supplementary files/Tab2.mp4" (from 0:57 to the end).
    * Notably, MMP offers a fast data input option in Tab2 as shown in the video "./MultiMSIProcessor-master/Supplementary files/txt files upload.mp4", which could skip the Tab1 data input option.

  * Tab3:<br />
    * After filling in pre-analysis parameters and clicking the "Processing" button, the following plots will be generated in the same folder of ROI.txt files:
      * 1.normalization_density.pdf
      * 2.normalization_ridge.pdf
      * 3.oplsda_model_building.pdf
      * 4.PCA_Plot.pdf
      * 5.PLSDA_Plot.pdf
      * This process was recorded in the "./MultiMSIProcessor-master/Supplementary files/Tab3.mp4" (till 0:41).
    * "Show boxplot and MS image" button shows the MS image and intensity boxplots.<br />
    ![pic5](https://github.com/biobibibi/MultiMSIProcessor/assets/53837584/b8df668f-70b2-4f72-816e-0463bfb3205a)

    * Clicking the "Match with the database" button helps users to choose the dictionary to annotate their m/zs. The dictionary is supplied in the "./MultiMSIProcessor-master/Supplementary files/5DB_Dictionary".
      * Users could easily DIY their dictionary for the annotation process.<br />
      * Sankey plot, Upset plot, and annotation results would be exported together into the folder where the dictionary is located.
    * The m/z list box on the left side of the "Match with the database" button also allows simple copy and paste to load the data.<br />
    * The "Export the m/z and P value" button could export the intensities of these significant mzs within the ROIs, together with its MSI image data.
    * The enrichment analysis is based on the R package "MSEA"[^1] and "FELLA"[^2] using KEGG ID. Please note that the number of provided input KEGG IDs is recommended to less than 100 (as shown in the video, we filtered the database annotation results before the enrichment analysis, and the filtered result was provided in the"./MultiMSIProcessor-master/Supplementary files/filtered_100_results.txt"). The enrichment databases are also provided in the "./MultiMSIProcessor-master/Supplementary files/metabolic_SMPDB_KEGG_id_for_MassInRaw". The FELLA_database is available following the [FELLA vignette](https://www.bioconductor.org/packages/release/bioc/vignettes/FELLA/inst/doc/FELLA.pdf).<br />
    ![pic6](https://github.com/biobibibi/MultiMSIProcessor/assets/53837584/15438bba-0357-4b6a-a911-7b368bf5a50c)

## 3. License and modifications
  * Multi MSI Processor is eager to develop and welcomes suggestions and rational critics.<br />
  * Multi MSI Processor (Copyright 2023 Siwei Bi, Manjiangcuo Wang, and Dan Du) is licensed under Apache 2.0.<br />
  * Please make a note of and respect the RawFileReader license in any modifications you make and wish to distribute.


[^1]: Xia J, Wishart D S. MSEA: a web-based tool to identify biologically meaningful patterns in quantitative metabolomic data[J]. Nucleic acids research, 2010, 38(suppl_2): W71-W77.
[^2]: Picart-Armada S, Fernández-Albert F, Vinaixa M, et al. FELLA: an R package to enrich metabolomics data[J]. BMC bioinformatics, 2018, 19: 1-9.
