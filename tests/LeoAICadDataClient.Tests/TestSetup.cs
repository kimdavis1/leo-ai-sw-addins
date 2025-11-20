using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LeoAICadDataClient.Tests
{
    /// <summary>
    /// Global test setup to disable Sentry during test execution.
    /// This prevents test errors from being sent to production Sentry.
    /// </summary>
    [TestClass]
    public class TestSetup
    {
        [AssemblyInitialize]
        public static void AssemblyInit(TestContext context)
        {
            // Disable Sentry for all tests
            Environment.SetEnvironmentVariable("DISABLE_SENTRY", "true");
            Console.WriteLine("Test Setup: Sentry disabled for test execution");
        }

        [AssemblyCleanup]
        public static void AssemblyCleanup()
        {
            // Clean up environment variable after tests
            Environment.SetEnvironmentVariable("DISABLE_SENTRY", null);
            Console.WriteLine("Test Cleanup: Sentry environment variable cleared");
        }
    }
}
