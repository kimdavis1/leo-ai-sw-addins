using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LeoAICadDataClient.Tests
{
    /// <summary>
    /// Tests for the 401 "auth token expired" detection + one-shot token-refresh retry added to
    /// <see cref="SecureApiClient"/> for SW-4285.
    ///
    /// Background: Descope JWTs can be revoked / force-expired server-side while still appearing valid
    /// locally by their <c>exp</c> claim, so leo-api returns HTTP 401
    /// <c>{"error":"Authorization token expired","isAuthTokenExpired":true}</c>. Before this fix the
    /// client neither force-refreshed the token nor retried — every such 401 went straight to Sentry
    /// (PDM-ADDIN-1N). The retry path forces a fresh token and retries exactly once.
    ///
    /// The pure detection helpers (<see cref="SecureApiClient.IsAuthTokenExpiredException"/> and
    /// <see cref="SecureApiClient.IsAuthTokenExpiredResponse"/>) are fully deterministic and run offline.
    ///
    /// The two <c>ExecuteWithRetryAsync</c> tests assert the call-count contract (retry exactly once).
    /// Their forced refresh calls <c>DescopeClient.ExchangeTokenAsync</c> against the real Descope endpoint
    /// with dummy credentials — this mirrors the existing <c>TokenRefreshConcurrencyTests</c>, which also
    /// make live calls and tolerate failures. The refresh is capped at 10s and swallows all errors
    /// (returns null), so the call-count assertions hold regardless of network availability; the only
    /// effect of an offline runner is a short delay.
    /// </summary>
    [TestClass]
    public class AuthTokenRetryTests
    {
        // ---- IsAuthTokenExpiredException (matches on the wrapped exception message) ----

        [TestMethod]
        public void IsAuthTokenExpiredException_DetectsLeoApi401ByMarkerInMessage()
        {
            Assert.IsTrue(SecureApiClient.IsAuthTokenExpiredException(
                new Exception("Authorization token expired (401) in GetSyncMetadata: {\"error\":\"Authorization token expired\",\"isAuthTokenExpired\":true}")));
            // Either marker on its own is sufficient.
            Assert.IsTrue(SecureApiClient.IsAuthTokenExpiredException(
                new Exception("server said {\"isAuthTokenExpired\":true}")));
            Assert.IsTrue(SecureApiClient.IsAuthTokenExpiredException(
                new Exception("Authorization token expired")));
        }

        [TestMethod]
        public void IsAuthTokenExpiredException_ReturnsFalseForUnrelatedExceptions()
        {
            Assert.IsFalse(SecureApiClient.IsAuthTokenExpiredException(new Exception("Rate limit (429): {\"retryAfterMs\":50}")));
            Assert.IsFalse(SecureApiClient.IsAuthTokenExpiredException(new Exception("Unauthorized")));
            Assert.IsFalse(SecureApiClient.IsAuthTokenExpiredException(new Exception("Connection refused")));
            Assert.IsFalse(SecureApiClient.IsAuthTokenExpiredException(new Exception(string.Empty)));
            Assert.IsFalse(SecureApiClient.IsAuthTokenExpiredException(null));
        }

        // ---- IsAuthTokenExpiredResponse (matches on status code + body, used by each method's branch) ----

        [TestMethod]
        public void IsAuthTokenExpiredResponse_MatchesOn401WithExpiredMarker()
        {
            const string body = "{\"error\":\"Authorization token expired\",\"isAuthTokenExpired\":true}";
            Assert.IsTrue(SecureApiClient.IsAuthTokenExpiredResponse(401, body));
            Assert.IsTrue(SecureApiClient.IsAuthTokenExpiredResponse(401, "{\"isAuthTokenExpired\":true}"));
            Assert.IsTrue(SecureApiClient.IsAuthTokenExpiredResponse(401, "Authorization token expired"));
        }

        [TestMethod]
        public void IsAuthTokenExpiredResponse_ReturnsFalseForNon401()
        {
            // Same body, but a non-401 status must NOT be treated as token-expiry (no spurious refresh/retry).
            const string body = "{\"error\":\"Authorization token expired\",\"isAuthTokenExpired\":true}";
            Assert.IsFalse(SecureApiClient.IsAuthTokenExpiredResponse(403, body));
            Assert.IsFalse(SecureApiClient.IsAuthTokenExpiredResponse(500, body));
        }

        [TestMethod]
        public void IsAuthTokenExpiredResponse_ReturnsFalseFor401WithoutMarker()
        {
            // A generic 401 (e.g. a bad API key) is NOT retryable — retrying would loop without succeeding.
            Assert.IsFalse(SecureApiClient.IsAuthTokenExpiredResponse(401, "{\"error\":\"Unauthorized\"}"));
            Assert.IsFalse(SecureApiClient.IsAuthTokenExpiredResponse(401, null));
            Assert.IsFalse(SecureApiClient.IsAuthTokenExpiredResponse(401, string.Empty));
        }

        // ---- ExecuteWithRetryAsync one-shot auth retry (call-count contract) ----

        [TestMethod]
        public async Task ExecuteWithRetryAsync_On401AuthExpired_ForcesRefreshAndRetriesOnce()
        {
            var client = new SecureApiClient("test-key", "test-project");
            int callCount = 0;

            int result = await client.ExecuteWithRetryAsync(async () =>
            {
                callCount++;
                if (callCount == 1)
                {
                    // First attempt: server rejects the token. Wrapped exactly like the production branch.
                    throw new Exception("Authorization token expired (401) in TestOp: {\"isAuthTokenExpired\":true}");
                }
                // Second attempt (after the forced refresh) succeeds.
                return await Task.FromResult(42);
            }, "TestAuthRetrySucceeds").ConfigureAwait(false);

            Assert.AreEqual(42, result);
            Assert.AreEqual(2, callCount, "Operation should run twice: original 401 + exactly one retry after the forced token refresh.");
        }

        [TestMethod]
        public async Task ExecuteWithRetryAsync_On401AuthExpired_SecondFailureBubblesAfterExactlyOneRetry()
        {
            var client = new SecureApiClient("test-key", "test-project");
            int callCount = 0;

            try
            {
                await client.ExecuteWithRetryAsync<int>(async () =>
                {
                    callCount++;
                    await Task.Yield();
                    // Persistent 401 — a refreshed token still gets rejected (a genuine auth failure).
                    throw new Exception("Authorization token expired (401) in TestOp: {\"isAuthTokenExpired\":true}");
                }, "TestAuthRetryPersistentFailure").ConfigureAwait(false);

                Assert.Fail("Expected the auth-expired exception to bubble after the single retry.");
            }
            catch (Exception ex)
            {
                Assert.IsTrue(SecureApiClient.IsAuthTokenExpiredException(ex),
                    "The bubbled exception should still be the auth-expired exception.");
            }

            Assert.AreEqual(2, callCount, "A persistent 401 must be retried EXACTLY once (original + one retry), never in a loop.");
        }

        [TestMethod]
        public async Task ExecuteWithRetryAsync_NonAuthExpired401LikeException_NotRetried()
        {
            // An exception that does not carry the auth-expiry markers (and is not a rate limit) must
            // bubble immediately without any refresh/retry — guards against treating every 401 as expiry.
            var client = new SecureApiClient("test-key", "test-project");
            int callCount = 0;

            try
            {
                await client.ExecuteWithRetryAsync<int>(async () =>
                {
                    callCount++;
                    await Task.Yield();
                    throw new Exception("Unauthorized (401): {\"error\":\"Invalid API key\"}");
                }, "TestNonExpiry401").ConfigureAwait(false);

                Assert.Fail("Expected the exception to bubble.");
            }
            catch (Exception ex)
            {
                Assert.IsFalse(SecureApiClient.IsAuthTokenExpiredException(ex));
                Assert.IsFalse(SecureApiClient.IsRateLimitException(ex));
            }

            Assert.AreEqual(1, callCount, "A non-expiry, non-rate-limit error must not be retried.");
        }
    }
}
