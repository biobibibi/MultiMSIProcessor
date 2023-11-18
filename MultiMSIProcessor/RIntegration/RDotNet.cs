// =========================
// This class was used in this program for integrating R inside the program and compatible with CCScriptNpp
// ---- Siwei Bi
// ==========================

using System.Text;

// Uses Dino  Esposito's Hook to embed the Graph Window
// http://msdn.microsoft.com/en-us/magazine/cc188920.aspx
// Dieter Menne, Menne Biomed Consulting, T bingen, dieter menne at menne-biomed de
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;


namespace MultiMSIProcessor.RIntegration
{

    #region Win32 Function Enums
    // ReSharper disable InconsistentNaming
    public enum RGraphHookAction
    {
        HCBT_ACTIVATE = 5
    }

    [Flags]
    public enum WindowPosFlag // Only those required here are included
    {
        SWP_FRAMECHANGED = 0x0020,
        SWP_NOOWNERZORDER = 0x0200,
        SWP_NOZORDER = 0x0004
    }

    [Flags]
    public enum WindowStyleFlag
    {
        WS_CHILDWINDOW = 0x40000000,
        WS_VISIBLE = 0x10000000
    }
    #endregion

    #region Class RGraphAppHook

    public class RGraphAppHook : LocalWindowsHook
    {
        // Events 
        protected Control parentControl;

        // Class constructor
        public RGraphAppHook() : base(HookType.WH_CBT)
        {
            HookInvoked += CbtHookInvoked;
        }

        public Control GraphControl
        {
            get { return parentControl; }
            set { parentControl = value; }
        }

        // Handles the hook event
        private void CbtHookInvoked(object sender, HookEventArgs e)
        {
            RGraphHookAction code = (RGraphHookAction)e.HookCode;
            if (code != RGraphHookAction.HCBT_ACTIVATE)
                return;
            StringBuilder lpClassName = new StringBuilder();
            GetClassName(e.wParam, lpClassName, 256);
            if (lpClassName.ToString() != "GraphApp")
                return;
            HandleActivateEvent(e.wParam);
        }

        // Handle the ACTIVATE hook event
        private void HandleActivateEvent(IntPtr wParam)
        {
            // Make window a child of the passed parent window control, e.g a Panel
            SetParent(wParam, GraphControl.Handle);
            SetWindowLong(wParam, -16, WindowStyleFlag.WS_VISIBLE | WindowStyleFlag.WS_CHILDWINDOW);
            SetWindowPos(wParam, IntPtr.Zero, 0, 0, GraphControl.Width, GraphControl.Height,
                WindowPosFlag.SWP_FRAMECHANGED | WindowPosFlag.SWP_NOOWNERZORDER | WindowPosFlag.SWP_NOZORDER);
        }
        // ReSharper restore UnusedParameter.Local

        #region Win32 Imports

        // ************************************************************************
        // Win32: GetClassName
        [DllImport("user32.dll")]
        protected static extern int GetClassName(IntPtr hwnd,
                                                 StringBuilder lpClassName, int nMaxCount);
        // Win32: SetParent
        [DllImport("user32.dll")]
        protected static extern int SetParent(IntPtr hwndChild, IntPtr hwdnNewParent);

        // Win32: SetWindowLong
        [DllImport("user32")]
        private static extern IntPtr SetWindowLong(IntPtr hWnd, int nIndex, WindowStyleFlag styleFlag);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy,
           WindowPosFlag posFlag);

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hWnd);
        #endregion
    }

    #endregion
}
