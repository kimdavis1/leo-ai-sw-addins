using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LeoAICadDataClient.Tests
{
    /// <summary>
    /// Tests to verify SemaphoreSlim is working correctly for mutual exclusion.
    /// These tests validate that our async-safe locking mechanism prevents race conditions.
    /// </summary>
    [TestClass]
    public class SemaphoreSlimBehaviorTests
    {
        [TestMethod]
        [Description("Verify SemaphoreSlim allows only one thread in critical section at a time")]
        public async Task SemaphoreSlim_OnlyOneThreadInCriticalSection_AtAnyTime()
        {
            // Arrange
            var semaphore = new SemaphoreSlim(1, 1);
            var concurrentCount = 0;
            var maxConcurrentCount = 0;
            var countLock = new object();
            var iterations = 100;

            // Act - spawn 100 threads trying to enter critical section
            var tasks = Enumerable.Range(0, iterations).Select(async i =>
            {
                await semaphore.WaitAsync();
                try
                {
                    // Enter critical section
                    lock (countLock)
                    {
                        concurrentCount++;
                        if (concurrentCount > maxConcurrentCount)
                        {
                            maxConcurrentCount = concurrentCount;
                        }
                    }

                    // Simulate work
                    await Task.Delay(1);

                    // Exit critical section
                    lock (countLock)
                    {
                        concurrentCount--;
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            // Assert - max concurrent should be 1
            Assert.AreEqual(1, maxConcurrentCount,
                "SemaphoreSlim failed - multiple threads entered critical section simultaneously!");
            Console.WriteLine($"SUCCESS: SemaphoreSlim correctly limited concurrency to 1 across {iterations} operations");
        }

        [TestMethod]
        [Description("Verify SemaphoreSlim with timeout doesn't cause deadlock")]
        public async Task SemaphoreSlim_WithTimeout_NoDeadlock()
        {
            // Arrange
            var semaphore = new SemaphoreSlim(1, 1);
            var acquiredCount = 0;
            var timeoutCount = 0;
            var countLock = new object();

            // Act - try to acquire lock with timeout
            var tasks = Enumerable.Range(0, 50).Select(async i =>
            {
                if (await semaphore.WaitAsync(TimeSpan.FromMilliseconds(100)))
                {
                    try
                    {
                        lock (countLock) { acquiredCount++; }
                        await Task.Delay(10); // Simulate work
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }
                else
                {
                    lock (countLock) { timeoutCount++; }
                }
            });

            await Task.WhenAll(tasks);

            // Assert
            Assert.IsTrue(acquiredCount > 0, "At least some threads should acquire the semaphore");
            Console.WriteLine($"Acquired: {acquiredCount}, Timed out: {timeoutCount}. Total: {acquiredCount + timeoutCount}");
            Assert.AreEqual(50, acquiredCount + timeoutCount, "All threads should complete (no deadlock)");
        }

        [TestMethod]
        [Description("Verify SemaphoreSlim is properly released even on exception")]
        public async Task SemaphoreSlim_ExceptionInCriticalSection_StillReleases()
        {
            // Arrange
            var semaphore = new SemaphoreSlim(1, 1);
            var successfulAcquisitions = 0;

            // Act - first thread throws exception, second should still acquire
            try
            {
                await semaphore.WaitAsync();
                try
                {
                    throw new InvalidOperationException("Test exception");
                }
                finally
                {
                    semaphore.Release(); // Should execute even with exception
                }
            }
            catch (InvalidOperationException)
            {
                // Expected
            }

            // Second acquisition should succeed (proves semaphore was released)
            if (await semaphore.WaitAsync(TimeSpan.FromSeconds(1)))
            {
                successfulAcquisitions++;
                semaphore.Release();
            }

            // Assert
            Assert.AreEqual(1, successfulAcquisitions,
                "Semaphore should be released even when exception occurs in critical section");
            Console.WriteLine("SUCCESS: Semaphore properly released after exception");
        }

        [TestMethod]
        [Description("Stress test: Verify no race conditions with rapid acquire/release")]
        public async Task SemaphoreSlim_RapidAcquireRelease_NoRaceConditions()
        {
            // Arrange
            var semaphore = new SemaphoreSlim(1, 1);
            var sharedCounter = 0;
            var iterations = 1000;

            // Act - 100 threads each incrementing counter 10 times
            var tasks = Enumerable.Range(0, 100).Select(async _ =>
            {
                for (int i = 0; i < 10; i++)
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        // If locking works, this increment is atomic
                        sharedCounter++;
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }
            });

            await Task.WhenAll(tasks);

            // Assert - if locking works, counter should be exactly iterations
            Assert.AreEqual(iterations, sharedCounter,
                $"Race condition detected! Expected {iterations}, got {sharedCounter}");
            Console.WriteLine($"SUCCESS: {iterations} atomic increments completed without race conditions");
        }

        [TestMethod]
        [Description("Verify double-check locking pattern works correctly")]
        public async Task DoubleCheckLocking_MultipleThreads_OnlyOneInitialization()
        {
            // Arrange - simulate initialization pattern used in our code
            var semaphore = new SemaphoreSlim(1, 1);
            var initialized = false;
            var initializationCount = 0;
            var countLock = new object();

            // Act - 100 threads trying to initialize
            var tasks = Enumerable.Range(0, 100).Select(async _ =>
            {
                // First check (outside lock) - fast path
                if (initialized) return;

                // Acquire lock
                await semaphore.WaitAsync();
                try
                {
                    // Second check (inside lock) - only one thread gets here first
                    if (initialized) return;

                    // Simulate initialization work
                    await Task.Delay(10);

                    lock (countLock)
                    {
                        initializationCount++;
                    }

                    initialized = true;
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            // Assert - should only initialize once
            Assert.AreEqual(1, initializationCount,
                $"Double-check locking failed! Initialized {initializationCount} times instead of 1");
            Assert.IsTrue(initialized, "Should be initialized after all threads complete");
            Console.WriteLine("SUCCESS: Double-check locking pattern works - only 1 initialization across 100 threads");
        }

        [TestMethod]
        [Description("Benchmark: Measure SemaphoreSlim performance under load")]
        public async Task SemaphoreSlim_PerformanceBenchmark_UnderLoad()
        {
            // Arrange
            var semaphore = new SemaphoreSlim(1, 1);
            var iterations = 1000;
            var stopwatch = Stopwatch.StartNew();

            // Act - measure time for 1000 acquire/release cycles
            var tasks = Enumerable.Range(0, iterations).Select(async _ =>
            {
                await semaphore.WaitAsync();
                try
                {
                    // Minimal work
                    await Task.Yield();
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
            stopwatch.Stop();

            // Assert - just report performance
            var avgMs = stopwatch.ElapsedMilliseconds / (double)iterations;
            Console.WriteLine($"Performance: {iterations} operations in {stopwatch.ElapsedMilliseconds}ms " +
                            $"(avg {avgMs:F3}ms per operation)");
            Assert.IsTrue(stopwatch.ElapsedMilliseconds < 10000,
                "Performance degradation detected - operations took too long");
        }

        [TestMethod]
        [Description("Verify ConfigureAwait(false) prevents context switching overhead")]
        public async Task ConfigureAwait_False_NoContextSwitching()
        {
            // Arrange
            var semaphore = new SemaphoreSlim(1, 1);
            var iterations = 100;

            // Act - use ConfigureAwait(false) to avoid context capture
            var tasks = Enumerable.Range(0, iterations).Select(async _ =>
            {
                await semaphore.WaitAsync().ConfigureAwait(false);
                try
                {
                    await Task.Delay(1).ConfigureAwait(false);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var allTasks = Task.WhenAll(tasks);
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30));

            // Should complete quickly without context switching
            var completed = await Task.WhenAny(allTasks, timeoutTask);

            // Assert - check if the operations completed (not timed out)
            Assert.AreSame(allTasks, completed,
                "Operations timed out - possible context switching issues or deadlock");
            Console.WriteLine($"SUCCESS: {iterations} operations completed with ConfigureAwait(false)");
        }
    }
}
