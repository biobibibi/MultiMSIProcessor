MMP_packages <- c("stats", "FactoMineR", "ggplot2", "patchwork", "factoextra",
                 "stats", "preprocessCore", "tidyr" ,"ggridges" , "forcats",
                 "limma", "ggpubr", "ggsignif", "rstatix", "tidyverse",
                 "circlize", "dplyr", "emmeans", "remotes", "devtools","exact2x2")
not_installed <- MMP_packages[!(MMP_packages %in% installed.packages()[ , "Package"])]    # Extract not installed packages
for(lib in not_installed){
      install.packages(lib)
  }

if (!require("BiocManager")) install.packages("BiocManager")
biocmanagerInstall_packages = c("mice", "ropls", "mixOmics", "ComplexHeatmap", "ggsankey", 
                                "ComplexUpset", "MSEApdata","MultiDataSet", "FELLA","Biostrings")

for(lib2 in biocmanagerInstall_packages){
  BiocManager::install(lib2)
}

remotes::install_github("rvlenth/emmeans")
devtools::install_github("davidsjoberg/ggsankey")

