using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using NLog;

namespace SectigoMetadataSync.Client;

public sealed class OAuthTokenClient
{
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    // Aggressive redaction for common secret-bearing fields / tokens
    private static readonly Regex _redactRegex = new(
        @"(?ix)
           (access[_-]?token) \s*[:=]\s*[""']? [^""'\s}]+
          |(refresh[_-]?token) \s*[:=]\s*[""']? [^""'\s}]+
          |(client[_-]?secret) \s*[:=]\s*[""']? [^""'\s}]+
          |(authorization)\s*:\s*bearer\s+[A-Za-z0-9\-\._~\+/=]+
        ", RegexOptions.Compiled);

    private readonly string? _audience; // optional
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly TimeSpan _defaultLifetime; // used when expires_in is absent
    private readonly HttpClient _http;
    private readonly string? _scopeCsv; // optional
    private readonly string _tokenUrl;

    public OAuthTokenClient(
        HttpClient http,
        string tokenUrl,
        string clientId,
        string clientSecret,
        string? scopesCsv = null,
        string? audience = null,
        TimeSpan? defaultLifetime = null)
    {
        _logger.Trace(
            "Entering OAuthTokenClient::.ctor (tokenUrl={tokenUrl}, hasScope={hasScope}, hasAudience={hasAudience})",
            Safe(tokenUrl), !string.IsNullOrWhiteSpace(scopesCsv), !string.IsNullOrWhiteSpace(audience));

        _http = http ?? throw new ArgumentNullException(nameof(http));
        _tokenUrl = tokenUrl ?? throw new ArgumentNullException(nameof(tokenUrl));
        _clientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
        _clientSecret = clientSecret ?? throw new ArgumentNullException(nameof(clientSecret));
        _scopeCsv = string.IsNullOrWhiteSpace(scopesCsv) ? null : scopesCsv;
        _audience = string.IsNullOrWhiteSpace(audience) ? null : audience;
        _defaultLifetime = defaultLifetime ?? TimeSpan.FromMinutes(5);

        // Be explicit about Accept; Content-Type is set by FormUrlEncodedContent.
        if (!_http.DefaultRequestHeaders.Accept.Any(h => h.MediaType == "application/json"))
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _logger.Trace("Exiting OAuthTokenClient::.ctor (defaultLifetime={lifetime})", _defaultLifetime);
    }

    /// <summary>
    ///     Mirrors:
    ///     curl/Invoke-RestMethod -X POST tokenUrl
    ///     Content-Type: application/x-www-form-urlencoded
    ///     grant_type=client_credentials&client_id=...&client_secret=...(&scope=...|audience=...)
    ///     Returns (accessToken, expiresUtc). If the IdP does not return expires_in, a conservative
    ///     synthetic expiry is applied (default 5 minutes).
    /// </summary>
    public (string accessToken, DateTimeOffset expiresUtc) GetTokenWithClientCredentials()
    {
        _logger.Trace("Entering GetTokenWithClientCredentials (url={url}, scopeSet={scopeSet}, audienceSet={audSet})",
            Safe(_tokenUrl), _scopeCsv is not null, _audience is not null);

        var form = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "client_credentials"),
            new("client_id", _clientId),
            new("client_secret", _clientSecret)
        };

        // Optional extras: many IdPs accept these; ignored if null/empty.
        if (!string.IsNullOrWhiteSpace(_scopeCsv))
            form.Add(new KeyValuePair<string, string>("scope", _scopeCsv!.Replace(",", " ").Trim()));
        if (!string.IsNullOrWhiteSpace(_audience))
            form.Add(new KeyValuePair<string, string>("audience", _audience!));

        using var req = new HttpRequestMessage(HttpMethod.Post, _tokenUrl)
        {
            Content = new FormUrlEncodedContent(form) // sets Content-Type: application/x-www-form-urlencoded
        };

        const int maxAttempts = 5;
        var attempt = 0;
        var rand = new Random();

        while (true)
        {
            attempt++;
            _logger.Debug("Token request attempt {attempt} -> {method} {url}", attempt, req.Method, Safe(_tokenUrl));

            using var resp = _http.Send(req);
            var sc = (int)resp.StatusCode;
            _logger.Debug("Token response attempt {attempt}: HTTP {status} ({reason})", attempt, sc, resp.ReasonPhrase);

            if ((int)resp.StatusCode is >= 200 and < 300)
            {
                using var s = resp.Content.ReadAsStream();
                var dto = JsonSerializer.Deserialize<TokenResponseMinimal>(s, _json)
                          ?? throw new InvalidOperationException("Token JSON was empty.");
                if (string.IsNullOrWhiteSpace(dto.AccessToken))
                {
                    _logger.Error("Token response missing access_token (attempt {attempt})", attempt);
                    throw new InvalidOperationException("Token response missing 'access_token'.");
                }

                var now = DateTimeOffset.UtcNow;
                // If expires_in is present, use it; else synthesize defaultLifetime.
                var expires = dto.ExpiresIn.HasValue && dto.ExpiresIn.Value > 0
                    ? now.AddSeconds(dto.ExpiresIn.Value)
                    : now.Add(_defaultLifetime);
                _logger.Info("Obtained OAuth token (type={type}).",
                    string.IsNullOrWhiteSpace(dto.TokenType) ? "bearer?" : dto.TokenType);
                _logger.Trace("Exiting GetTokenWithClientCredentials (expiresUtc={expires:o})", expires);
                return (dto.AccessToken!, expires);
            }

            // Transient handling (rate limit / server errors)
            if ((resp.StatusCode == (HttpStatusCode)429 || (int)resp.StatusCode >= 500) && attempt < maxAttempts)
            {
                var delayMs = GetRetryAfterMs(resp) ??
                              (int)Math.Min(30000, Math.Pow(2, attempt) * 250 + rand.Next(0, 250));
                _logger.Warn(
                    "Transient token failure (HTTP {status}). Retrying attempt {nextAttempt}/{max} after {delay} ms.",
                    sc, attempt + 1, maxAttempts, delayMs);
                Thread.Sleep(delayMs);
                continue;
            }

            var body = ReadBody(resp);
            _logger.Error("Token request failed (HTTP {status} {reason}). Body: {body}",
                sc, resp.ReasonPhrase, body);
            throw new InvalidOperationException(
                $"Token request failed ({(int)resp.StatusCode} {resp.ReasonPhrase}). Body: {body}");
        }

        static int? GetRetryAfterMs(HttpResponseMessage resp)
        {
            if (resp.Headers.TryGetValues("Retry-After", out var values))
            {
                var v = values.FirstOrDefault();
                if (int.TryParse(v, out var secs) && secs >= 0) return secs * 1000;
                if (DateTimeOffset.TryParse(v, out var when))
                    return (int)Math.Max(0, (when - DateTimeOffset.UtcNow).TotalMilliseconds);
            }

            return null;
        }

        static string ReadBody(HttpResponseMessage resp)
        {
            using var s = resp.Content.ReadAsStream();
            using var sr = new StreamReader(s, Encoding.UTF8, true, 8192, false);
            return sr.ReadToEnd();
        }
    }

    private static string Safe(string? s)
    {
        return string.IsNullOrWhiteSpace(s) ? "(null)" : s.Length > 2048 ? s[..2048] + "…" : s;
    }

    private sealed class TokenResponseMinimal
    {
        // Exact match for {"access_token":"..."}
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }

        // Optional fields that some IdPs include
        [JsonPropertyName("expires_in")] public int? ExpiresIn { get; set; }

        [JsonPropertyName("token_type")] public string? TokenType { get; set; }

        // Capture any other fields without failing deserialization
        [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

        /// <summary>
        ///     Best-effort normalization for alternate casings (e.g., "accessToken").
        ///     Call after deserialization if you need to be defensive.
        /// </summary>
        public void Normalize()
        {
            if (!string.IsNullOrWhiteSpace(AccessToken) || Extra is null) return;

            if (Extra.TryGetValue("accessToken", out var alt) && alt.ValueKind == JsonValueKind.String)
                AccessToken = alt.GetString();

            if (!ExpiresIn.HasValue &&
                Extra.TryGetValue("expiresIn", out var exp) && exp.ValueKind == JsonValueKind.Number &&
                exp.TryGetInt32(out var secs))
                ExpiresIn = secs;

            if (string.IsNullOrWhiteSpace(TokenType) &&
                Extra.TryGetValue("tokenType", out var tt) && tt.ValueKind == JsonValueKind.String)
                TokenType = tt.GetString();
        }
    }
}

public static class OAuthServiceCollectionExtensions
{
    // inside: public static class OAuthServiceCollectionExtensions
    public static IServiceCollection AddKeyfactorMetadataClientOAuth(
        this IServiceCollection services,
        string baseAddress,
        OAuthOptions oauthOptions,
        string requestedWith = "APIClient")
    {
        if (oauthOptions is null) throw new ArgumentNullException(nameof(oauthOptions));
        if (string.IsNullOrWhiteSpace(baseAddress)) throw new ArgumentNullException(nameof(baseAddress));

        // 1) Ensure the OAuth token client is registered with its ctor args
        services.AddHttpClient<OAuthTokenClient>(c =>
            {
                c.Timeout = TimeSpan.FromSeconds(30);
                c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddTypedClient(http => new OAuthTokenClient(
                http,
                oauthOptions.TokenUrl,
                oauthOptions.ClientId,
                oauthOptions.ClientSecret,
                oauthOptions.ScopesCsv,
                oauthOptions.Audience));

        // 2) Register the handler itself (so DI can resolve it)
        services.AddTransient<OAuthTokenHandler>(sp =>
            new OAuthTokenHandler(
                sp.GetRequiredService<OAuthTokenClient>(),
                oauthOptions,
                requestedWith));

        // 3) Register the Keyfactor client and plug the handler into the pipeline
        services.AddHttpClient<KeyfactorMetadataClient>(client =>
            {
                client.BaseAddress = new Uri(baseAddress);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddHttpMessageHandler<OAuthTokenHandler>();

        return services;
    }

    public sealed class OAuthOptions
    {
        public string TokenUrl { get; init; } = string.Empty;
        public string ClientId { get; init; } = string.Empty;
        public string ClientSecret { get; init; } = string.Empty;
        public string? ScopesCsv { get; init; } // e.g., "openid,profile"
        public string? Audience { get; init; }
        public int refreshSkewSeconds { get; init; } = 120;
    }
}

internal sealed class OAuthTokenHandler : DelegatingHandler
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly OAuthServiceCollectionExtensions.OAuthOptions _opt;
    private readonly string _requestedWith;
    private readonly OAuthTokenClient _tokenClient;
    private string? _accessToken;
    private DateTimeOffset _expiresUtc;

    public OAuthTokenHandler(
        OAuthTokenClient tokenClient,
        OAuthServiceCollectionExtensions.OAuthOptions opt,
        string requestedWith)
    {
        _tokenClient = tokenClient ?? throw new ArgumentNullException(nameof(tokenClient));
        _opt = opt ?? throw new ArgumentNullException(nameof(opt));
        _requestedWith = string.IsNullOrWhiteSpace(requestedWith) ? "APIClient" : requestedWith;
    }

    // -------- SYNC PIPELINE --------
    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken ct)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        _logger.Trace("-> OAuthTokenHandler.Send: {method} {uri}", request.Method, request.RequestUri);

        EnsureToken(ct);
        ApplyHeaders(request);

        var resp = base.Send(request, ct);
        _logger.Debug("Downstream response: HTTP {status}", (int)resp.StatusCode);

        if (resp.StatusCode == HttpStatusCode.Unauthorized)
        {
            _logger.Warn("401 Unauthorized detected. Forcing token refresh and retrying once.");
            resp.Dispose();
            ForceRefresh(ct);
            ApplyHeaders(request, true);
            resp = base.Send(request, ct);
            _logger.Debug("Retry response: HTTP {status}", (int)resp.StatusCode);
        }

        if (resp.IsSuccessStatusCode)
        {
            _logger.Trace("<- OAuthTokenHandler.Send OK [{status}]", (int)resp.StatusCode);
            return resp; // caller disposes
        }

        // Non-success: read body synchronously, log, then throw with full body in exception
        var status = resp.StatusCode;
        var reason = resp.ReasonPhrase ?? string.Empty;

        var body = string.Empty;
        try
        {
            if (resp.Content != null)
            {
                using var s = resp.Content.ReadAsStream(); // sync path on .NET 8
                using var sr = new StreamReader(s, Encoding.UTF8, true, 8192, false);
                body = sr.ReadToEnd();
            }
        }
        catch
        {
            // Swallow body-read failures; keep body empty for logging/exception.
        }

        var snippet = body.Length > 2000 ? body[..2000] + "…[truncated]" : body;
        string? reqId = null;
        try
        {
            if (!resp.Headers.TryGetValues("x-request-id", out var v) || (reqId = v.FirstOrDefault()) is null)
                if (resp.Headers.TryGetValues("request-id", out var v2))
                    reqId = v2.FirstOrDefault();
        }
        catch
        {
            /* ignore header parsing issues */
        }

        _logger.Error(
            "OAuth downstream failure {method} {uri}: {code} {reason}. RequestId={reqId}. Body={body}",
            request.Method, request.RequestUri, (int)status, reason, reqId ?? "n/a", snippet);

        resp.Dispose();
        _logger.Trace("<- OAuthTokenHandler.Send throwing for {method} {uri}", request.Method, request.RequestUri);

        throw new HttpRequestException(
            $"HTTP {(int)status} {reason} for {request.RequestUri}. RequestId={reqId}. Body: {body}",
            null,
            status);
    }


    private void ApplyHeaders(HttpRequestMessage req, bool replaceAuth = false)
    {
        if (replaceAuth || req.Headers.Authorization is null)
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        if (!req.Headers.Contains("x-keyfactor-requested-with"))
            req.Headers.Add("x-keyfactor-requested-with", _requestedWith);
        _logger.Trace("Applied auth + headers (replaceAuth={replaceAuth})", replaceAuth);
    }

    private void EnsureToken(CancellationToken ct)
    {
        if (!IsExpiringSoon())
        {
            _logger.Trace("Token considered fresh (exp={exp:o})", _expiresUtc);
            return;
        }

        _logger.Debug("Token missing/expiring soon. Acquiring under lock.");
        _gate.Wait(ct);
        try
        {
            if (!IsExpiringSoon())
            {
                _logger.Trace("Token became fresh while waiting (exp={exp:o})", _expiresUtc);
                return;
            }

            var (tkn, expires) = _tokenClient.GetTokenWithClientCredentials();
            _accessToken = tkn;
            _expiresUtc = expires;
            _logger.Info("Token refreshed (exp={exp:o}, ttlSec≈{ttl})",
                _expiresUtc, (int)(_expiresUtc - DateTimeOffset.UtcNow).TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to acquire OAuth token.");
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void ForceRefresh(CancellationToken ct)
    {
        _logger.Debug("Forcing token refresh.");
        _gate.Wait(ct);
        try
        {
            var (tkn, expires) = _tokenClient.GetTokenWithClientCredentials();
            _accessToken = tkn;
            _expiresUtc = expires;
            _logger.Info("Token forced refresh complete (exp={exp:o})", _expiresUtc);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Forced token refresh failed.");
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool IsExpiringSoon()
    {
        var soon = string.IsNullOrWhiteSpace(_accessToken)
                   || DateTimeOffset.UtcNow >=
                   _expiresUtc - TimeSpan.FromSeconds(Math.Max(30, _opt.refreshSkewSeconds));
        _logger.Trace("IsExpiringSoon? {soon} (now={now:o}, exp={exp:o})", soon, DateTimeOffset.UtcNow, _expiresUtc);
        return soon;
    }
}