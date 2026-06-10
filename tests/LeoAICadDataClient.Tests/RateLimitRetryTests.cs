using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LeoAICadDataClient.Tests
{
    /// <summary>
    /// Tests for the rate-limit retry helpers in <see cref="SecureApiClient"/>.
    /// Exercises the server-supplied <c>retryAfterMs</c> handling and the exponential fallback.
    /// </summary>
    [TestClass]
    public class RateLimitRetryTests
    {
        [TestMethod]
        public void ParseRetryAfterMs_ExtractsValueFromLeoApi429Body()
        {
            string message = "Rate limit (429): {\"error\":\"Too many requests for this resource. Please try again later.\",\"retryAfterMs\":2699}";

            int? result = SecureApiClient.ParseRetryAfterMs(message);

            Assert.IsTrue(result.HasValue);
            Assert.AreEqual(2699, result.Value);
        }

        [TestMethod]
        public void ParseRetryAfterMs_ToleratesWhitespaceAroundColon()
        {
            string message = "Rate limit (429): { \"error\" : \"x\" , \"retryAfterMs\" :   42 }";

            int? result = SecureApiClient.ParseRetryAfterMs(message);

            Assert.AreEqual(42, result);
        }

        [TestMethod]
        public void ParseRetryAfterMs_ReturnsNullWhenFieldAbsent()
        {
            // The monolith advisory-lock 429 does not carry retryAfterMs.
            string message = "Rate limit (429): {\"error\":\"Another request is already processing this file. Please retry.\"}";

            int? result = SecureApiClient.ParseRetryAfterMs(message);

            Assert.IsNull(result);
        }

        [TestMethod]
        public void ParseRetryAfterMs_ReturnsNullForEmptyOrNullInput()
        {
            Assert.IsNull(SecureApiClient.ParseRetryAfterMs(null));
            Assert.IsNull(SecureApiClient.ParseRetryAfterMs(string.Empty));
        }

        [TestMethod]
        public void ComputeRateLimitDelayMs_PrefersServerValueOverExponentialFallback()
        {
            string message = "Rate limit (429): {\"retryAfterMs\":500}";

            int delay = SecureApiClient.ComputeRateLimitDelayMs(message, exponentialFallbackMs: 8000, out bool usedServerValue);

            Assert.IsTrue(usedServerValue);
            // server 500 + 100ms buffer = 600ms base, positive-only jitter 0..+15% → range 600..690.
            Assert.IsTrue(delay >= 600 && delay <= 690, $"Delay {delay} outside expected jitter band 600..690");
        }

        [TestMethod]
        public void ComputeRateLimitDelayMs_NeverReturnsBelowServerRetryAfterMs()
        {
            // Hammer the jitter to confirm the positive-only jitter for server-supplied values
            // never produces a delay below the server's floor — otherwise we'd retry before the
            // bucket reset and guarantee another 429.
            int[] serverValues = { 12, 44, 500, 2699 };
            foreach (int serverMs in serverValues)
            {
                string message = $"Rate limit (429): {{\"retryAfterMs\":{serverMs}}}";
                for (int i = 0; i < 200; i++)
                {
                    int delay = SecureApiClient.ComputeRateLimitDelayMs(message, exponentialFallbackMs: 1000, out _);
                    Assert.IsTrue(delay >= serverMs, $"Delay {delay} dropped below the server's retryAfterMs of {serverMs}");
                }
            }
        }

        [TestMethod]
        public void ComputeRateLimitDelayMs_FallsBackToExponentialWhenServerOmitsRetryAfter()
        {
            string message = "Rate limit (429): {\"error\":\"Another request is already processing this file. Please retry.\"}";

            int delay = SecureApiClient.ComputeRateLimitDelayMs(message, exponentialFallbackMs: 2000, out bool usedServerValue);

            Assert.IsFalse(usedServerValue);
            // base 2000, ±15% jitter -> 1700..2300.
            Assert.IsTrue(delay >= 1700 && delay <= 2300, $"Delay {delay} outside expected jitter band 1700..2300");
        }

        [TestMethod]
        public void ComputeRateLimitDelayMs_NginxHtml429FallsBackToFiveSecondBaseline()
        {
            // Real shape observed in staging: nginx returns a bare HTML body with no JSON,
            // no retryAfterMs field. The C# wrapper preserves this in the exception message.
            // The fallback path must trigger and use the 5s baseline (or larger as exponential
            // doubles on subsequent retries).
            string nginxBody = "Rate limit (429): <html>\n<head><title>429 Too Many Requests</title></head>\n<body>\n<center><h1>429 Too Many Requests</h1></center>\n<hr><center>nginx</center>\n</body>\n</html>\n";

            // First retry uses the initial baseline (5000ms).
            int delay = SecureApiClient.ComputeRateLimitDelayMs(nginxBody, exponentialFallbackMs: 5000, out bool usedServerValue);

            Assert.IsFalse(usedServerValue, "nginx HTML body has no retryAfterMs -- must fall back to exponential");
            // base 5000, symmetric ±15% jitter -> 4250..5750.
            Assert.IsTrue(delay >= 4250 && delay <= 5750, $"Delay {delay} outside expected first-retry band 4250..5750");
        }

        [TestMethod]
        public void ComputeRateLimitDelayMs_ClampsServerValueToMaxRetryDelay()
        {
            // 120s exceeds the 60s cap (which matches leo-api's largest legitimate window).
            string message = "Rate limit (429): {\"retryAfterMs\":120000}";

            int delay = SecureApiClient.ComputeRateLimitDelayMs(message, exponentialFallbackMs: 1000, out _);

            // 60_000 base, positive-only jitter 0..+15% → range 60000..69000.
            Assert.IsTrue(delay >= 60000 && delay <= 69000, $"Delay {delay} should be clamped near the 60s cap, got {delay}");
        }

        [TestMethod]
        public void ComputeRateLimitDelayMs_EnforcesFiftyMsFloorForTinyServerValues()
        {
            // Server says 12ms, observed in real Sentry events. With a 100ms buffer the base is 112ms,
            // but negative jitter (~-16) could otherwise produce values below the 50ms floor.
            string message = "Rate limit (429): {\"retryAfterMs\":12}";

            for (int i = 0; i < 100; i++)
            {
                int delay = SecureApiClient.ComputeRateLimitDelayMs(message, exponentialFallbackMs: 1000, out _);
                Assert.IsTrue(delay >= 50, $"Delay {delay} dropped below the 50ms floor");
            }
        }

        [TestMethod]
        public void ComputeRateLimitDelayMs_AddsBufferToServerValue()
        {
            // Verify the +100ms buffer is applied so we don't race the bucket reset, and that
            // jitter is positive-only on top of it.
            string message = "Rate limit (429): {\"retryAfterMs\":1000}";

            long total = 0;
            const int iterations = 500;
            for (int i = 0; i < iterations; i++)
            {
                total += SecureApiClient.ComputeRateLimitDelayMs(message, exponentialFallbackMs: 1000, out _);
            }
            double mean = total / (double)iterations;
            // base = 1000 + 100 buffer = 1100; jitter is uniform on [0, 165], mean ≈ 82.5;
            // expected mean ≈ 1182.5. Allow ±25ms slack for sample variance.
            Assert.IsTrue(mean >= 1157 && mean <= 1208, $"Mean delay {mean} not centered near expected 1182.5ms");
        }

        [TestMethod]
        public void IsRateLimitException_DetectsLeoApi429ByMessageContent()
        {
            Assert.IsTrue(SecureApiClient.IsRateLimitException(new Exception("Rate limit (429): {\"retryAfterMs\":2699}")));
            Assert.IsTrue(SecureApiClient.IsRateLimitException(new Exception("Server returned 429 Too Many Requests")));
            Assert.IsTrue(SecureApiClient.IsRateLimitException(new Exception("rate limit exceeded")));
        }

        [TestMethod]
        public void IsRateLimitException_ReturnsFalseForUnrelatedExceptions()
        {
            Assert.IsFalse(SecureApiClient.IsRateLimitException(new Exception("Connection refused")));
            Assert.IsFalse(SecureApiClient.IsRateLimitException(new Exception("Internal server error 500")));
            Assert.IsFalse(SecureApiClient.IsRateLimitException(new Exception(string.Empty)));
            Assert.IsFalse(SecureApiClient.IsRateLimitException(null));
        }

        [TestMethod]
        public async Task ExecuteWithRetryAsync_RetriesUntilTransient429sResolve()
        {
            var client = new SecureApiClient("test-key", "test-project");
            int callCount = 0;

            int result = await client.ExecuteWithRetryAsync(async () =>
            {
                callCount++;
                if (callCount < 3)
                {
                    // 50ms server-supplied delay keeps the test fast (max ~3 × 150ms = 450ms wall time).
                    throw new Exception("Rate limit (429): {\"retryAfterMs\":50}");
                }
                return await Task.FromResult(42);
            }, "TestOperation").ConfigureAwait(false);

            Assert.AreEqual(42, result);
            Assert.AreEqual(3, callCount, "Operation should be invoked 3 times: first call + 2 retries before success.");
        }

        [TestMethod]
        public async Task ExecuteWithRetryAsync_ThrowsAfterRetryExhaustion()
        {
            var client = new SecureApiClient("test-key", "test-project");
            int callCount = 0;

            try
            {
                await client.ExecuteWithRetryAsync<int>(async () =>
                {
                    callCount++;
                    await Task.Yield();
                    // Use a very small retryAfterMs so the test isn't slow — 5 retries × ~150ms ≈ 750ms.
                    throw new Exception("Rate limit (429): {\"retryAfterMs\":50}");
                }, "AlwaysFails").ConfigureAwait(false);

                Assert.Fail("Expected exhausted-retry exception to be thrown.");
            }
            catch (Exception ex)
            {
                Assert.IsTrue(SecureApiClient.IsRateLimitException(ex), "Exhaustion should re-throw the original rate-limit exception.");
            }

            Assert.AreEqual(6, callCount, "Operation should be invoked MaxRetries+1 = 6 times before giving up.");
        }

        [TestMethod]
        public async Task ExecuteWithRetryAsync_DoesNotRetryNonRateLimitExceptions()
        {
            var client = new SecureApiClient("test-key", "test-project");
            int callCount = 0;

            try
            {
                await client.ExecuteWithRetryAsync<int>(async () =>
                {
                    callCount++;
                    await Task.Yield();
                    throw new InvalidOperationException("Some unrelated failure");
                }, "NonRateLimitOp").ConfigureAwait(false);

                Assert.Fail("Expected InvalidOperationException to bubble up.");
            }
            catch (InvalidOperationException)
            {
                // Expected
            }

            Assert.AreEqual(1, callCount, "Non-rate-limit exceptions should NOT be retried.");
        }
    }
}
