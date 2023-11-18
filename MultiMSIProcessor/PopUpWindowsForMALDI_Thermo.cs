using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using ThermoFisher.CommonCore.Data.Business;

namespace MultiMSIProcessor
{
    public partial class PopUpWindowsForMALDI_Thermo : Form
    {
        public int xRange;
        public int yRange;

        public PopUpWindowsForMALDI_Thermo()
        {
            InitializeComponent();
        }

        private void buttonEnter_Click(object sender, EventArgs e)
        {
            _ = int.TryParse(textBoxX.Text, out xRange);
            _ = int.TryParse(textBoxY.Text, out yRange);
            Close();
        }
    }
}
