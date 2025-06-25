using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace WpfGattTerminal
{
    public enum RpcAuthnLevel
    {
        Default = 0,
        None = 1,
        Connect = 2,
        Call = 3,
        Pkt = 4,
        PktIntegrity = 5,
        PktPrivacy = 6
    }

    public enum RpcImpLevel
    {
        Default = 0,
        Anonymous = 1,
        Identify = 2,
        Impersonate = 3,
        Delegate = 4
    }

    public enum EoAuthnCap
    {
        None = 0x00,
        MutualAuth = 0x01,
        StaticCloaking = 0x20,
        DynamicCloaking = 0x40,
        AnyAuthority = 0x80,
        MakeFullSIC = 0x100,
        Default = 0x800,
        SecureRefs = 0x02,
        AccessControl = 0x04,
        AppID = 0x08,
        Dynamic = 0x10,
        RequireFullSIC = 0x200,
        AutoImpersonate = 0x400,
        NoCustomMarshal = 0x2000,
        DisableAAA = 0x1000
    }

    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        [System.Runtime.InteropServices.DllImport("ole32.dll")]
        public static extern int CoInitializeSecurity(
            IntPtr pVoid, int cAuthSvc, IntPtr asAuthSvc, IntPtr pReserved1, RpcAuthnLevel level,
            RpcImpLevel impers, IntPtr pAuthList, EoAuthnCap dwCapabilities, IntPtr pReserved3);

        public int resInitSec;

        // Handling Unhandled Exceptions in WPF (The most complete collection of handlers)
        // Ref1: https://code.msdn.microsoft.com/windowsdesktop/Handling-Unhandled-47492d0b
        // Ref2: http://www.pinvoke.net/default.aspx/ole32.coinitializesecurity
        public App()
        {
            resInitSec = CoInitializeSecurity(
                IntPtr.Zero, -1, IntPtr.Zero, IntPtr.Zero, RpcAuthnLevel.None,
                RpcImpLevel.Impersonate, IntPtr.Zero, EoAuthnCap.None, IntPtr.Zero);

            DispatcherUnhandledException += App_DispatcherUnhandledException; //Example 2
        }

        // Example 2
        void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            //Don't use MessageBox or anything before actually save the error, 
            // it may itself cause a threading issue and fail to save the original error.
            MessageBox.Show($"UnhandledException: {e.Exception.Message}");
            e.Handled = true;
        }
    }
}
