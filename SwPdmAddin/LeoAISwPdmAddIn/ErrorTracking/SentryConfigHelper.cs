using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace LeoAISwPdmAddIn.ErrorTracking
{
    /// <summary>
    /// Helper class for configuring Sentry DSN in Windows Registry.
    /// </summary>
    [ComVisible(false)]
    public static class SentryConfigHelper
    {
        // This class is not used in the main add-in flow but provided for manual configuration
        // It's kept simple to avoid any COM type library issues
    }
}
