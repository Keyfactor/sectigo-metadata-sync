using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using SectigoMetadataSync.Models;

namespace SectigoMetadataSync.Client;

/// <summary>
///     Fully synchronous Sectigo API client using HttpClient.Send with automatic retry + backoff.
///     Safe for use via IHttpClientFactory (typed client) or manual construction.
/// </summary>
public sealed class SectigoClient
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    // ---- JSON options
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly TimeSpan _baseDelay;
    private readonly HttpClient _http;
    private readonly TimeSpan _maxDelay;

    // ---- Retry/backoff policy (tune as desired)
    private readonly int _maxRetries;
    private readonly Random _rng = new();

    /// <param name="http">
    ///     HttpClient with BaseAddress set to your Sectigo endpoint (e.g., https://cert-manager.com/api/).
    /// </param>
    /// <param name="maxRetries">Total attempts = maxRetries + 1 initial.</param>
    /// <param name="baseDelay">Initial backoff delay when Retry-After is absent.</param>
    /// <param name="maxDelay">Ceiling for backoff delay.</param>
    public SectigoClient(HttpClient http, int maxRetries = 6, TimeSpan? baseDelay = null, TimeSpan? maxDelay = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _maxRetries = Math.Max(0, maxRetries);
        _baseDelay = baseDelay ?? TimeSpan.FromMilliseconds(500);
        _maxDelay = maxDelay ?? TimeSpan.FromSeconds(20);

        // Recommended: JSON headers default
        if (!_http.DefaultRequestHeaders.Accept.Contains(new MediaTypeWithQualityHeaderValue("application/json")))
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>
    ///     Configure per-request authentication headers (Sectigo SCM style).
    /// </summary>
    public void Authenticate(string login, string password, string customerUri)
    {
        var h = _http.DefaultRequestHeaders;
        h.Remove("login");
        h.Add("login", login ?? string.Empty);
        h.Remove("password");
        h.Add("password", password ?? string.Empty);
        h.Remove("customerUri");
        h.Add("customerUri", customerUri ?? string.Empty);

        Log.Info("Sectigo auth headers configured (login/customerUri set).");
    }

    // ---------- Public API surface (mirror of common operations) ----------

    public List<SectigoCustomField> ListCustomFields()
    {
        return SendJson<List<SectigoCustomField>>(HttpMethod.Get, "api/customField/v2")
               ?? new List<SectigoCustomField>();
    }

    public List<SectigoCertificate> GetCertificatesByProfileId(List<int> profileIds,
        bool includeRevokedAndExpired = false, int pageSize = 25)
    {
        if (profileIds == null || profileIds.Count == 0)
            throw new ArgumentException("profileIds required", nameof(profileIds));
        var acc = new List<SectigoCertificate>();
        foreach (var pid in profileIds)
            // If includeRevokedAndExpired=false, Sectigo typically returns current/issued with no explicit status filter.
            acc.AddRange(
                GetCertificatesByProfileIdAndStatus(pid, includeRevokedAndExpired ? null : "Issued", pageSize));
        return acc;
    }

    public SectigoCertificateDetails GetCertificateDetails(int sectigoCertId)
    {
        return SendJson<SectigoCertificateDetails>(HttpMethod.Get, $"api/ssl/v1/{sectigoCertId}")
               ?? throw new InvalidOperationException($"Certificate {sectigoCertId} not found.");
    }

    public SectigoCertificateDetails UpdateCertificateMetadata(
        int sslId,
        List<CustomFieldDetails>? customFields = null,
        string? comments = null)
    {
        var payload = new { sslId, customFields, comments };
        return SendJson<SectigoCertificateDetails>(HttpMethod.Put, "api/ssl/v1", payload)
               ?? throw new InvalidOperationException("Null response when updating certificate metadata.");
    }

    // ---------- Internals ----------

    private List<SectigoCertificate> GetCertificatesByProfileIdAndStatus(int profileId, string? status, int pageSize)
    {
        var position = 0;
        var acc = new List<SectigoCertificate>();
        while (true)
        {
            var qs = $"sslTypeId={profileId}&position={position}&size={pageSize}";
            if (!string.IsNullOrWhiteSpace(status)) qs += $"&status={Uri.EscapeDataString(status)}";

            var page = SendJson<List<SectigoCertificate>>(HttpMethod.Get, $"api/ssl/v1?{qs}") ??
                       new List<SectigoCertificate>();
            acc.AddRange(page);
            if (page.Count < pageSize) break;
            position += pageSize;
        }

        return acc;
    }

    /// <summary>
    ///     Core JSON sender (request body optional). Fully synchronous.
    /// </summary>
    private TOut? SendJson<TOut>(HttpMethod method, string relativeUrl, object? body = null)
    {
        using var req = new HttpRequestMessage(method, relativeUrl);

        if (body != null)
        {
            var json = JsonSerializer.Serialize(body, JsonOpts);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var res = SendWithRetry(req);
        var text = res.Content is null ? null : ReadString(res.Content);

        if (string.IsNullOrWhiteSpace(text))
            return default;

        try
        {
            return JsonSerializer.Deserialize<TOut>(text!, JsonOpts);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "JSON deserialization error for {Url}. Payload (truncated): {Snippet}",
                relativeUrl, text!.Length > 512 ? text.Substring(0, 512) + "…" : text);
            throw;
        }
    }

    private HttpResponseMessage SendWithRetry(HttpRequestMessage request)
    {
        _ = request ?? throw new ArgumentNullException(nameof(request));

        var attempt = 0;
        var nextDelay = _baseDelay;

        bool IsIdempotent(HttpMethod m)
        {
            return m == HttpMethod.Get || m == HttpMethod.Head || m == HttpMethod.Put || m == HttpMethod.Delete;
        }

        while (true)
        {
            attempt++;
            var start = DateTimeOffset.UtcNow;

            HttpResponseMessage? res = null;
            Exception? sendEx = null;

            try
            {
                res = _http.Send(request, HttpCompletionOption.ResponseHeadersRead);
            }
            catch (Exception ex)
            {
                sendEx = ex;
            }

            // Network failure -> retry as transient (respect attempt budget)
            if (sendEx != null)
            {
                if (attempt > _maxRetries)
                {
                    Log.Error(sendEx, "Too many retries ({Attempt}/{Max}) for {Method} {Uri}.", attempt, _maxRetries,
                        request.Method, request.RequestUri);
                    throw new HttpRequestException(
                        $"Network send failed after {attempt} attempts for {request.Method} {request.RequestUri}.",
                        sendEx);
                }

                SleepWithJitter(ref nextDelay);
                Log.Warn(sendEx, "Transient send error on attempt {Attempt} for {Method} {Uri}. Retrying.",
                    attempt, request.Method, request.RequestUri);
                continue;
            }

            // Success path
            if (res!.IsSuccessStatusCode)
            {
                Log.Trace("HTTP {Status} in {Ms}ms for {Method} {Uri}",
                    (int)res.StatusCode, (DateTimeOffset.UtcNow - start).TotalMilliseconds,
                    request.Method, request.RequestUri);
                return res; // caller disposes
            }

            // Decide whether to retry based on status
            var sc = (int)res.StatusCode;
            var retryable =
                sc == 429 || // too many requests (rate limited)
                sc == 503 || // service unavailable
                sc == 408 || sc == 500 || sc == 502 || sc == 504; // classic transient errors

            // For non-idempotent methods, only retry if explicitly rate-limited with Retry-After
            if (!IsIdempotent(request.Method) && retryable && sc != 429)
                retryable = false;

            // Honor Retry-After (seconds or HTTP-date) if present
            var retryAfter = ParseRetryAfter(res);
            if (retryAfter > TimeSpan.Zero) retryable = true;

            if (!retryable || attempt > _maxRetries)
            {
                // Read body fully for exception message; log truncated snippet
                var reason = res.ReasonPhrase ?? string.Empty;
                var fullBody = res.Content is null ? string.Empty : ReadString(res.Content);
                var snippet = fullBody.Length > 2000 ? fullBody.Substring(0, 2000) + "…[truncated]" : fullBody;

                string? reqId = null;
                try
                {
                    if (!res.Headers.TryGetValues("x-request-id", out var v) || (reqId = v.FirstOrDefault()) is null)
                        if (res.Headers.TryGetValues("request-id", out var v2))
                            reqId = v2.FirstOrDefault();
                }
                catch
                {
                    /* ignore header parsing issues */
                }

                Log.Error(
                    "HTTP {Status} not retryable or attempts exhausted ({Attempt}/{Max}). {Method} {Uri}. RequestId={ReqId}. Body={Body}",
                    sc, attempt, _maxRetries, request.Method, request.RequestUri, reqId ?? "n/a", snippet);

                // Dispose before throwing since we won't return it
                res.Dispose();

                throw new HttpRequestException(
                    $"HTTP {sc} {reason} for {request.Method} {request.RequestUri}. RequestId={reqId}. Body: {fullBody}",
                    null,
                    (HttpStatusCode)sc);
            }

            // Sleep based on server guidance first
            if (retryAfter > TimeSpan.Zero)
            {
                Log.Warn("Rate limited ({Status}). Honoring Retry-After={RetryAfter}. Attempt {Attempt}/{Max}.",
                    sc, retryAfter, attempt, _maxRetries);
                Thread.Sleep(retryAfter);
            }
            else
            {
                // Alternatively, respect X-RateLimit-Reset if available (epoch seconds)
                var resetAt = ParseRateLimitReset(res);
                if (resetAt > DateTimeOffset.UtcNow)
                {
                    var toWait = resetAt - DateTimeOffset.UtcNow;
                    Log.Warn("Rate hint via X-RateLimit-Reset. Sleeping {Wait}. Attempt {Attempt}/{Max}.",
                        toWait, attempt, _maxRetries);
                    Thread.Sleep(toWait);
                }
                else
                {
                    // Exponential backoff + Full-Jitter
                    SleepWithJitter(ref nextDelay);
                }
            }

            // Dispose the non-success response before the next attempt
            res.Dispose();
        }
    }

    private void SleepWithJitter(ref TimeSpan nextDelay)
    {
        // Full-Jitter: sleep = random(0, min(cap, base * 2^attempt))
        var cap = _maxDelay;
        var max = nextDelay < cap ? nextDelay : cap;
        var millis = _rng.Next(0, (int)Math.Max(1, max.TotalMilliseconds));
        Thread.Sleep(TimeSpan.FromMilliseconds(millis));

        // Increase for next time
        var doubled = TimeSpan.FromMilliseconds(Math.Min(cap.TotalMilliseconds, nextDelay.TotalMilliseconds * 2.0));
        nextDelay = doubled;
    }

    private static TimeSpan ParseRetryAfter(HttpResponseMessage res)
    {
        if (res.Headers.TryGetValues("Retry-After", out var values))
        {
            var ra = values.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(ra)) return TimeSpan.Zero;

            // Seconds?
            if (int.TryParse(ra.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var secs) && secs >= 0)
                return TimeSpan.FromSeconds(secs);

            // HTTP-date?
            if (DateTimeOffset.TryParseExact(
                    ra.Trim(),
                    new[] { "r", "ddd, dd MMM yyyy HH':'mm':'ss 'GMT'" }, // RFC1123
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var when) && when > DateTimeOffset.UtcNow)
                return when - DateTimeOffset.UtcNow;
        }

        return TimeSpan.Zero;
    }

    private static DateTimeOffset ParseRateLimitReset(HttpResponseMessage res)
    {
        // Optional: X-RateLimit-Reset (epoch seconds)
        if (res.Headers.TryGetValues("X-RateLimit-Reset", out var vals))
        {
            var v = vals.FirstOrDefault();
            if (long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch) && epoch > 0)
                try
                {
                    return DateTimeOffset.FromUnixTimeSeconds(epoch);
                }
                catch
                {
                    /* ignore */
                }
        }

        return DateTimeOffset.MinValue;
    }

    private static string ReadString(HttpContent content)
    {
        return content.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
    }
}

/// <summary>
///     DI registration helpers.
/// </summary>
public static class SectigoServiceCollectionExtensions
{
    public static IServiceCollection AddSectigoClient(this IServiceCollection services, string baseAddress)
    {
        services.AddHttpClient<SectigoClient>(client =>
        {
            client.BaseAddress = new Uri(baseAddress);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });
        return services;
    }
}