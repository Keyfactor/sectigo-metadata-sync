// Copyright 2024 Keyfactor
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
// You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
// and limitations under the License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using SectigoMetadataSync.Client;
using SectigoMetadataSync.Models;

namespace SectigoMetadataSync.Client
{
    /// <summary>
    ///     Fully synchronous Keyfactor client implemented around HttpClient.Send (NET 8+).
    /// </summary>
    public class KeyfactorMetadataClient
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly HttpClient _httpClient;

        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private DateTimeOffset _bearerExpiresUtc;
        private string? _bearerToken;

        public KeyfactorMetadataClient(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        // OAuth support
        public void AuthenticateBearer(string accessToken, DateTimeOffset? expiresUtc = null,
            string requestedWith = "APIClient")
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                throw new ArgumentException("Access token must be provided.", nameof(accessToken));

            _bearerToken = accessToken;
            _bearerExpiresUtc =
                expiresUtc ?? DateTimeOffset.UtcNow.AddMinutes(5); // conservative default if none provided

            var headers = _httpClient.DefaultRequestHeaders;
            headers.Authorization = new AuthenticationHeaderValue("Bearer", _bearerToken);
            headers.Remove("x-keyfactor-requested-with");
            headers.Add("x-keyfactor-requested-with", requestedWith);

            // Remove any stale Basic header if caller switched modes
            if (headers.TryGetValues("Authorization", out _))
            {
                // Nothing else to do; new Bearer replaces any previous Authorization.
            }
        }

        // Ensure token validity before API calls
        private void EnsureBearer(Func<(string token, DateTimeOffset expiresUtc)> refresh)
        {
            if (string.IsNullOrEmpty(_bearerToken) || DateTimeOffset.UtcNow >= _bearerExpiresUtc.AddMinutes(-2))
            {
                var (tkn, exp) = refresh();
                AuthenticateBearer(tkn, exp);
            }
        }


        /// <summary>
        ///     Configure basic auth and required headers.
        /// </summary>
        public void Authenticate(string username, string password, string requestedWith = "APIClient")
        {
            if (username is null) throw new ArgumentNullException(nameof(username));
            if (password is null) throw new ArgumentNullException(nameof(password));

            var creds = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}"));
            var headers = _httpClient.DefaultRequestHeaders;
            headers.Authorization = new AuthenticationHeaderValue("Basic", creds);
            headers.Remove("x-keyfactor-requested-with");
            headers.Add("x-keyfactor-requested-with", requestedWith);
        }

        // ---- Helpers ------------------------------------------------------
        private Uri BuildUri(string relativePath, string? query = null)
        {
            var baseUri = _httpClient.BaseAddress ??
                          throw new InvalidOperationException("HttpClient.BaseAddress must be set.");
            var path = relativePath.TrimStart('/') + (string.IsNullOrEmpty(query)
                ? string.Empty
                : (relativePath.Contains('?') ? "&" : "?") + query);
            return new Uri(baseUri, path);
        }

        private HttpResponseMessage Send(HttpMethod method, string relativePath, HttpContent? content = null)
        {
            if (method is null) throw new ArgumentNullException(nameof(method));
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new ArgumentException("relativePath cannot be null or whitespace.", nameof(relativePath));

            var uri = BuildUri(relativePath);
            _logger.Trace("-> Send: {0} {1}", method, uri);

            using var req = new HttpRequestMessage(method, uri);
            if (content != null) req.Content = content;

            HttpResponseMessage resp = null!;
            try
            {
                // Prefer headers-first so we control when/how we buffer the body.
                resp = _httpClient.Send(req, HttpCompletionOption.ResponseHeadersRead);

                if (resp.IsSuccessStatusCode)
                {
                    _logger.Trace("<- Send OK: {0} {1} [{2}]", method, uri, (int)resp.StatusCode);
                    return resp; // caller disposes
                }

                // Non-success: read body synchronously, log, then throw with full message
                var status = resp.StatusCode;
                var reason = resp.ReasonPhrase ?? string.Empty;
                var body = ReadString(resp); // uses Content.ReadAsStream() (sync)

                var snippet = body.Length > 2000 ? body.Substring(0, 2000) + "…[truncated]" : body;
                _logger.Error("HTTP failure {0} {1}: {2} {3}. Body={4}",
                    method, uri, (int)status, reason, snippet);

                resp.Dispose(); // not returning it
                throw new HttpRequestException(
                    $"HTTP {(int)status} {reason} for {uri}. Body: {body}",
                    null,
                    status);
            }
            finally
            {
                _logger.Trace("<- Send exit: {0} {1}", method, uri);
            }
        }


        private T? ReadJson<T>(HttpResponseMessage resp)
        {
            using var s = resp.Content.ReadAsStream(); // synchronous
            return JsonSerializer.Deserialize<T>(s, _jsonOptions);
        }

        private string ReadString(HttpResponseMessage resp)
        {
            using var s = resp.Content.ReadAsStream(); // synchronous
            using var sr = new StreamReader(s, Encoding.UTF8, true, 8192, false);
            return sr.ReadToEnd();
        }

        private StringContent JsonBody<T>(T value)
        {
            return new StringContent(JsonSerializer.Serialize(value, _jsonOptions), Encoding.UTF8, "application/json");
        }

        // ---- API methods --------------------------------------------------

        /// <summary>
        ///     Lists metadata field definitions.
        /// </summary>
        public List<KeyfactorMetadataField> ListMetadataFields()
        {
            var resp = Send(HttpMethod.Get, "MetadataFields");
            return ReadJson<List<KeyfactorMetadataField>>(resp) ?? new List<KeyfactorMetadataField>();
        }

        /// <summary>
        ///     Sends/Upserts unified metadata fields to Keyfactor.
        /// </summary>
        public void SendUnifiedMetadataFields(List<UnifiedFormatField> unifiedFields,
            List<KeyfactorMetadataField> existingFields)
        {
            if (unifiedFields is null || unifiedFields.Count == 0)
                throw new ArgumentException("The list of unified metadata fields cannot be null or empty.",
                    nameof(unifiedFields));

            existingFields ??= new List<KeyfactorMetadataField>();

            var created = 0;
            var updated = 0;

            foreach (var field in unifiedFields)
                try
                {
                    var existing = existingFields.FirstOrDefault(f =>
                        f.Name.Equals(field.KeyfactorMetadataFieldName, StringComparison.OrdinalIgnoreCase));
                    KeyfactorMetadataField payload;
                    HttpResponseMessage resp;
                    if (existing is not null)
                    {
                        // PUT: include Id
                        payload = new KeyfactorMetadataField
                        {
                            Id = existing.Id,
                            Name = field.KeyfactorMetadataFieldName,
                            Description = field.KeyfactorDescription,
                            DataType = field.KeyfactorDataType,
                            Hint = field.KeyfactorHint,
                            Validation = field.KeyfactorValidation,
                            Enrollment = field.KeyfactorEnrollment,
                            Message = field.KeyfactorMessage,
                            Options = field.KeyfactorOptions != null ? string.Join(",", field.KeyfactorOptions) : null,
                            DefaultValue = field.KeyfactorDefaultValue,
                            DisplayOrder = field.KeyfactorDisplayOrder,
                            CaseSensitive = field.KeyfactorCaseSensitive
                        };
                        var json = JsonSerializer.Serialize(payload, _jsonOptions);
                        _logger.Trace($"Sending JSON Payload: {json}");
                        resp = Send(HttpMethod.Put, "MetadataFields", JsonBody(payload));
                        updated++;
                    }
                    else
                    {
                        // POST: do not include Id
                        payload = new KeyfactorMetadataField
                        {
                            Id = 0,
                            Name = field.KeyfactorMetadataFieldName,
                            Description = field.KeyfactorDescription,
                            DataType = field.KeyfactorDataType,
                            Hint = field.KeyfactorHint,
                            Validation = field.KeyfactorValidation,
                            Enrollment = field.KeyfactorEnrollment,
                            Message = field.KeyfactorMessage,
                            Options = field.KeyfactorOptions != null ? string.Join(",", field.KeyfactorOptions) : null,
                            DefaultValue = field.KeyfactorDefaultValue,
                            DisplayOrder = field.KeyfactorDisplayOrder,
                            CaseSensitive = field.KeyfactorCaseSensitive
                        };
                        var json = JsonSerializer.Serialize(payload, _jsonOptions);
                        _logger.Trace($"Sending JSON Payload: {json}");
                        resp = Send(HttpMethod.Post, "MetadataFields", JsonBody(payload));
                        created++;
                    }


                    var returned = ReadJson<KeyfactorMetadataField>(resp);
                    if (returned is not null)
                    {
                        field.KeyfactorMetadataFieldId = returned.Id;
                        _logger.Trace(
                            $"Field '{field.KeyfactorMetadataFieldName}' updated with KeyfactorMetadataFieldId: {field.KeyfactorMetadataFieldId}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Error processing metadata field: {field.KeyfactorMetadataFieldName}");
                }

            _logger.Info($"Metadata fields processed: {created} created, {updated} updated.");
        }


        /// <summary>
        ///     Get certificates by issuer (paged).
        ///     Optionally restrict to certs that were added (imported) on/after <paramref name="addedSince" />.
        /// </summary>
        public List<KeyfactorCertificate> GetCertificatesByIssuer(
            string issuerSubstring,
            bool includeRevokedAndExpired,
            int pageNumber,
            int pageSize,
            string? addedSince = null)
        {
            if (string.IsNullOrWhiteSpace(issuerSubstring))
                throw new ArgumentException("Issuer substring cannot be null or empty.", nameof(issuerSubstring));

            if (pageNumber <= 0) pageNumber = 1;
            if (pageSize <= 0) pageSize = 100;

            // Base query
            var q = $"IssuerDN -contains \"{issuerSubstring}\"";


            var encoded = Uri.EscapeDataString(q);

            var query = new StringBuilder()
                .Append("QueryString=").Append(encoded)
                .Append("&includeMetadata=true")
                .Append("&PageReturned=").Append(pageNumber)
                .Append("&ReturnLimit=").Append(pageSize)
                .Append("&SortField=NotBefore")
                .Append("&SortAscending=1"); // keep existing behavior

            if (!string.IsNullOrEmpty(addedSince)) query.Append($"&DateImported -ge \"{addedSince}\"");

            if (includeRevokedAndExpired)
                query.Append("&IncludeRevoked=true&IncludeExpired=true");

            var resp = Send(HttpMethod.Get, $"Certificates?{query}");

            var json = ReadString(resp);
            _logger.Trace($"Raw JSON Response from GetCertificatesByIssuer: {json}");

            try
            {
                return JsonSerializer.Deserialize<List<KeyfactorCertificate>>(json, _jsonOptions)
                       ?? new List<KeyfactorCertificate>();
            }
            catch (JsonException ex)
            {
                _logger.Error(ex, "Failed to deserialize the certificate list from Keyfactor API.");
                throw new InvalidOperationException("Failed to deserialize the certificate list from Keyfactor API.",
                    ex);
            }
        }

        /// <summary>
        ///     Update certificate metadata (typed payload). Prefer this overload.
        ///     Sends integers and booleans as JSON numbers/bools; other types as strings.
        /// </summary>
        public bool UpdateCertificateMetadata(int certificateId, IReadOnlyDictionary<string, object?> metadata)
        {
            if (certificateId <= 0)
                throw new ArgumentException("Certificate ID must be greater than zero.", nameof(certificateId));
            if (metadata is null || metadata.Count == 0)
                throw new ArgumentException("Metadata cannot be null or empty.", nameof(metadata));

            // Final pass: sanitize strings, normalize numeric/bool types, drop null/blank values.
            var normalized = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in metadata)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key))
                    continue;

                var v = NormalizeForWire(kvp.Value, out var keep);
                if (keep)
                    normalized[kvp.Key] = v;
            }

            if (normalized.Count == 0)
                throw new ArgumentException("No non-empty metadata values after normalization.", nameof(metadata));

            var body = new { Id = certificateId, Metadata = normalized };

            // Log the exact JSON we're about to send (useful for troubleshooting type issues).
            var json = JsonSerializer.Serialize(body, _jsonOptions);
            _logger.Trace($"Sending JSON Payload to update metadata: {json}");

            try
            {
                var resp = Send(HttpMethod.Put, "Certificates/Metadata", JsonBody(body));
                using (resp)
                {
                    /* disposing response */
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to update metadata for certificate ID: {certificateId}");
                return false;
            }

            // ---- local helpers ----

            static object? NormalizeForWire(object? value, out bool keep)
            {
                keep = true;
                if (value is null)
                {
                    keep = false;
                    return null;
                }

                switch (value)
                {
                    // Already-typed primitives pass through
                    case bool b:
                        return b;

                    case sbyte or byte or short or ushort or int or uint or long:
                        // cast all signed/unsigned integrals to long for uniform JSON emission
                        return Convert.ToInt64(value, CultureInfo.InvariantCulture);

                    case ulong ul:
                        // STJ writes ulong; Keyfactor integer fields are signed. Clamp if needed.
                        if (ul > long.MaxValue) ul = long.MaxValue;
                        return (long)ul;

                    case float f:
                        // If it’s integral, keep as integer; else round toward zero
                        return (long)f;

                    case double d:
                        return (long)d;

                    case decimal m:
                        return (long)m;

                    case DateTime dt:
                        // Use ISO-8601 UTC string
                        return dt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

                    case DateTimeOffset dto:
                        return dto.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

                    case string s:
                    {
                        var cleaned = SanitizeString(s);
                        if (string.IsNullOrWhiteSpace(cleaned))
                        {
                            keep = false;
                            return null;
                        }

                        return cleaned;
                    }

                    // If a JsonElement slipped through, preserve its JSON scalar type where possible.
                    case JsonElement el:
                    {
                        switch (el.ValueKind)
                        {
                            case JsonValueKind.Null:
                            case JsonValueKind.Undefined:
                                keep = false;
                                return null;
                            case JsonValueKind.True: return true;
                            case JsonValueKind.False: return false;
                            case JsonValueKind.Number:
                                if (el.TryGetInt64(out var n)) return n;
                                if (el.TryGetDouble(out var dbl)) return (long)dbl;
                                return SanitizeString(el.GetRawText());
                            case JsonValueKind.String:
                                var s = el.GetString();
                                var cleaned = SanitizeString(s);
                                if (string.IsNullOrWhiteSpace(cleaned))
                                {
                                    keep = false;
                                    return null;
                                }

                                return cleaned;
                            default:
                                // objects/arrays should have been flattened earlier; keep raw JSON as string
                                var raw = el.GetRawText();
                                var cleanedJson = SanitizeString(raw, false);
                                if (string.IsNullOrWhiteSpace(cleanedJson))
                                {
                                    keep = false;
                                    return null;
                                }

                                return cleanedJson;
                        }
                    }

                    default:
                        // Fallback: ToString() + sanitize (avoids exceptions on unexpected types)
                        var txt = SanitizeString(value.ToString());
                        if (string.IsNullOrWhiteSpace(txt))
                        {
                            keep = false;
                            return null;
                        }

                        return txt;
                }
            }

            static string SanitizeString(string? s, bool collapseInnerWhitespace = true)
            {
                if (string.IsNullOrEmpty(s)) return string.Empty;

                // Normalize Unicode, drop hidden/zero-width/bidi, replace NBSP, remove control chars (except CR/LF/TAB)
                Span<char> hidden = stackalloc char[]
                {
                    '\u200B', '\u200C', '\u200D', '\uFEFF', '\u200E', '\u200F', '\u202A', '\u202B', '\u202C', '\u202D',
                    '\u202E'
                };
                var norm = s.Normalize(NormalizationForm.FormKC);
                var sb = new StringBuilder(norm.Length);
                foreach (var ch in norm)
                {
                    var isHidden = false;
                    for (var i = 0; i < hidden.Length; i++)
                        if (ch == hidden[i])
                        {
                            isHidden = true;
                            break;
                        }

                    if (isHidden) continue;

                    if (ch == '\u00A0')
                    {
                        sb.Append(' ');
                        continue;
                    } // NBSP -> space

                    if (char.IsControl(ch) && ch != '\r' && ch != '\n' && ch != '\t') continue;
                    sb.Append(ch);
                }

                var cleaned = sb.ToString().Trim();

                if (!collapseInnerWhitespace) return cleaned;

                // Collapse all whitespace to single spaces
                return Regex.Replace(cleaned, @"\s+", " ").Trim();
            }
        }
    }
}

/// <summary>
///     DI registration helpers.
/// </summary>
public static class KeyfactorServiceCollectionExtensions
{
    public static IServiceCollection AddKeyfactorMetadataClient(this IServiceCollection services, string baseAddress)
    {
        services.AddHttpClient<KeyfactorMetadataClient>(client =>
        {
            client.BaseAddress = new Uri(baseAddress);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });
        return services;
    }
}