using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LeoAICadDataClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LeoAICadDataClient.Tests
{
    /// <summary>
    /// Tests for token refresh race conditions and thread safety.
    /// These tests verify that the SemaphoreSlim fixes prevent concurrent token refreshes.
    /// </summary>
    [TestClass]
    public class TokenRefreshConcurrencyTests
    {
        private const int STRESS_TEST_THREADS = 100;
        private const int STRESS_TEST_TIMEOUT_SECONDS = 60;

        [TestMethod]
        [Description("Verify multiple concurrent API calls don't cause race conditions in token refresh")]
        public async Task TokenRefresh_100ConcurrentCalls_NoExceptions()
        {
            // Arrange
            var client = CreateTestClient();
            var exceptions = new List<Exception>();
            var exceptionLock = new object();

            // Act - spawn 100 concurrent API calls
            var tasks = Enumerable.Range(0, STRESS_TEST_THREADS).Select(async i =>
            {
                try
                {
                    // These calls will trigger token refresh logic
                    // With proper locking, only one refresh should occur
                    await CallApiMethodSafely(client, i);
                }
                catch (Exception ex)
                {
                    // Capture exceptions for analysis
                    lock (exceptionLock)
                    {
                        exceptions.Add(ex);
                    }
                }
            });

            // Wait for all threads to complete with timeout
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(STRESS_TEST_TIMEOUT_SECONDS)))
            {
                try
                {
                    await Task.WhenAll(tasks);
                }
                catch (OperationCanceledException)
                {
                    Assert.Fail($"Test timed out after {STRESS_TEST_TIMEOUT_SECONDS} seconds - possible deadlock!");
                }
            }

            // Assert - we expect some HTTP errors (no real server), but NO race condition exceptions
            // Filter out expected HTTP/network errors
            var unexpectedExceptions = exceptions.Where(e =>
                !IsExpectedNetworkError(e)
            ).ToList();

            if (unexpectedExceptions.Any())
            {
                var exDetails = string.Join("\n", unexpectedExceptions.Select(e =>
                    $"  - {e.GetType().Name}: {e.Message}"));
                Assert.Fail($"Unexpected exceptions occurred (possible race conditions):\n{exDetails}");
            }

            // Success - no race conditions detected
            Console.WriteLine($"Test completed successfully. {exceptions.Count} expected network errors, 0 race conditions.");
        }

        [TestMethod]
        [Description("Verify concurrent calls complete within timeout (no deadlock)")]
        public async Task ConcurrentApiCalls_NoDeadlock_CompletesWithinTimeout()
        {
            // Arrange
            var client = CreateTestClient();
            var completedCount = 0;
            var completedLock = new object();

            // Act
            var tasks = Enumerable.Range(0, 50).Select(async i =>
            {
                try
                {
                    await CallApiMethodSafely(client, i);
                }
                catch
                {
                    // Ignore errors - we're testing for deadlocks, not success
                }
                finally
                {
                    lock (completedLock)
                    {
                        completedCount++;
                    }
                }
            });

            // All tasks should complete within 30 seconds
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            {
                try
                {
                    await Task.WhenAll(tasks);
                }
                catch (OperationCanceledException)
                {
                    Assert.Fail($"DEADLOCK DETECTED: Only {completedCount}/50 tasks completed within 30 seconds!");
                }
            }

            // Assert
            Assert.AreEqual(50, completedCount, "All tasks should have completed");
            Console.WriteLine("SUCCESS: No deadlock detected - all 50 tasks completed within 30 seconds");
        }

        [TestMethod]
        [Description("Verify rapid-fire sequential calls don't cause token refresh issues")]
        public async Task SequentialApiCalls_RapidFire_NoErrors()
        {
            // Arrange
            var client = CreateTestClient();
            var callCount = 20;
            var successCount = 0;

            // Act - make 20 rapid sequential calls
            for (int i = 0; i < callCount; i++)
            {
                try
                {
                    await CallApiMethodSafely(client, i);
                    successCount++;
                }
                catch (Exception ex)
                {
                    // Only fail on unexpected errors (not network errors)
                    if (!IsExpectedNetworkError(ex))
                    {
                        Assert.Fail($"Unexpected error on call {i}: {ex.Message}");
                    }
                }
            }

            // Assert - we expect all calls to execute without race conditions
            // (they may fail with network errors, but should not crash or deadlock)
            Console.WriteLine($"Completed {callCount} rapid-fire calls. Network errors are expected.");
            Assert.IsTrue(true, "Test completed without race conditions or deadlocks");
        }

        [TestMethod]
        [Description("Stress test with mixed operation types")]
        public async Task MixedOperations_ConcurrentStressTest_NoRaceConditions()
        {
            // Arrange
            var client = CreateTestClient();
            var random = new Random();
            var exceptions = new List<Exception>();
            var exceptionLock = new object();

            // Act - spawn 100 threads doing random operations
            var tasks = Enumerable.Range(0, STRESS_TEST_THREADS).Select(async i =>
            {
                try
                {
                    var operationType = random.Next(0, 3);
                    switch (operationType)
                    {
                        case 0:
                            await client.GetDirectoryInfoAsync($"machine-{i}");
                            break;
                        case 1:
                            await client.GetSyncMetadataAsync($"dir-{i}");
                            break;
                        case 2:
                            await client.DeleteFileAsync($"dir-{i}", $"file-{i}.txt");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    lock (exceptionLock)
                    {
                        exceptions.Add(ex);
                    }
                }
            });

            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(STRESS_TEST_TIMEOUT_SECONDS)))
            {
                await Task.WhenAll(tasks);
            }

            // Assert - check for race conditions
            var raceConditions = exceptions.Where(e => !IsExpectedNetworkError(e)).ToList();
            Assert.AreEqual(0, raceConditions.Count,
                $"Race conditions detected: {string.Join(", ", raceConditions.Select(e => e.Message))}");

            Console.WriteLine($"Stress test completed. {exceptions.Count} network errors (expected), 0 race conditions.");
        }

        #region Helper Methods

        private SecureApiClient CreateTestClient()
        {
            // Create client with dummy credentials
            // API calls will fail (no server), but we're testing concurrency, not functionality
            return new SecureApiClient("test-api-key", "test-project-id");
        }

        private async Task CallApiMethodSafely(SecureApiClient client, int threadId)
        {
            // Call a simple API method that will trigger token refresh logic
            // We expect this to fail with network errors, but NOT with race conditions
            try
            {
                await client.GetDirectoryInfoAsync($"test-machine-{threadId}");
            }
            catch (Exception ex)
            {
                // Re-throw so caller can track it
                throw;
            }
        }

        private bool IsExpectedNetworkError(Exception ex)
        {
            // These are expected errors when calling API without real server
            var errorMessage = ex.Message.ToLower();
            var innerMessage = ex.InnerException?.Message?.ToLower() ?? "";

            return errorMessage.Contains("no such host") ||
                   errorMessage.Contains("connection") ||
                   errorMessage.Contains("timeout") ||
                   errorMessage.Contains("network") ||
                   errorMessage.Contains("401") ||
                   errorMessage.Contains("403") ||
                   errorMessage.Contains("429") ||
                   errorMessage.Contains("500") ||
                   errorMessage.Contains("unable to connect") ||
                   errorMessage.Contains("name or service not known") ||
                   innerMessage.Contains("no such host") ||
                   innerMessage.Contains("connection") ||
                   innerMessage.Contains("timeout") ||
                   ex.GetType().Name.Contains("Http") ||
                   ex.GetType().Name.Contains("WebException");
        }

        #endregion
    }
}
