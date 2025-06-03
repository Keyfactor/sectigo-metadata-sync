// Copyright 2021 Keyfactor
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
// You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
// and limitations under the License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using SectigoMetadataSync.Client;
using SectigoMetadataSync.Models;

namespace SectigoMetadataSync.Client
{
    /// <summary>
    ///     Synchronous client for retrieving Keyfactor metadata fields.
    ///     Designed for use as a typed HttpClient via IHttpClientFactory.
    /// </summary>
    public class KeyfactorMetadataClient
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly HttpClient _httpClient;

        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        ///     Initializes a new instance of the KeyfactorMetadataClient.
        ///     HttpClient is injected/configured via IHttpClientFactory; its BaseAddress should be set externally.
        /// </summary>
        public KeyfactorMetadataClient(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        /// <summary>
        ///     Configures Basic authentication and required headers for Keyfactor API.
        ///     Must be called before invoking any list/get methods.
        /// </summary>
        /// <param name="username">Keyfactor API username</param>
        /// <param name="password">Keyfactor API password</param>
        /// <param name="requestedWith">Value for the x-keyfactor-requested-with header (default: APIClient)</param>
        public void Authenticate(string username, string password, string requestedWith = "APIClient")
        {
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}"));
            var headers = _httpClient.DefaultRequestHeaders;

            headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            headers.Remove("x-keyfactor-requested-with");
            headers.Add("x-keyfactor-requested-with", requestedWith);
        }

        /// <summary>
        ///     Lists metadata field definitions. Maps to GET /KeyfactorAPI/MetadataFields with optional filtering, paging, and
        ///     sorting.
        /// </summary>
        public List<KeyfactorMetadataField> ListMetadataFields()
        {
            var sb = new StringBuilder(_httpClient.BaseAddress + "/MetadataFields");
            var response = _httpClient.GetAsync(sb.ToString()).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonSerializer.Deserialize<List<KeyfactorMetadataField>>(json, _jsonOptions)
                   ?? new List<KeyfactorMetadataField>();
        }


        /// <summary>
        ///     Sends a list of unified metadata fields to Keyfactor.
        ///     If a field already exists, it is updated using an HTTP PUT request.
        ///     Otherwise, it is created using an HTTP POST request.
        /// </summary>
        /// <param name="unifiedFields">List of unified metadata fields to send.</param>
        /// <param name="existingFields">List of existing metadata fields in Keyfactor.</param>
        public void SendUnifiedMetadataFields(List<UnifiedFormatField> unifiedFields,
            List<KeyfactorMetadataField> existingFields)
        {
            if (unifiedFields == null || unifiedFields.Count == 0)
                throw new ArgumentException("The list of unified metadata fields cannot be null or empty.");

            var createdCount = 0;
            var updatedCount = 0;

            foreach (var field in unifiedFields)
                try
                {
                    // Check if the field already exists in Keyfactor
                    var existingField = existingFields.FirstOrDefault(f =>
                        f.Name.Equals(field.KeyfactorMetadataFieldName, StringComparison.OrdinalIgnoreCase));

                    // Convert UnifiedFormatField to KeyfactorMetadataField
                    var keyfactorField = new KeyfactorMetadataField
                    {
                        Id = existingField?.Id ?? 0, // Use existing ID if available, otherwise 0 for new field
                        Name = field.KeyfactorMetadataFieldName,
                        Description = field.KeyfactorDescription,
                        DataType = field.KeyfactorDataType,
                        Hint = field.KeyfactorHint,
                        Validation = field.KeyfactorValidation,
                        Enrollment = field.KeyfactorEnrollment,
                        Message = field.KeyfactorMessage,
                        Options = field.KeyfactorOptions != null
                            ? string.Join(",", field.KeyfactorOptions)
                            : null, // Convert array to string
                        DefaultValue = field.KeyfactorDefaultValue,
                        DisplayOrder = field.KeyfactorDisplayOrder,
                        CaseSensitive = field.KeyfactorCaseSensitive
                    };

                    // Serialize the field to JSON
                    var jsonContent = JsonSerializer.Serialize(keyfactorField, _jsonOptions);
                    var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                    // Log the JSON payload being sent
                    _logger.Trace($"Sending JSON Payload: {jsonContent}");

                    HttpResponseMessage response;
                    if (existingField != null)
                    {
                        // Field exists, update it using PUT
                        var endpoint = $"{_httpClient.BaseAddress}/MetadataFields";
                        response = _httpClient.PutAsync(endpoint, content).GetAwaiter().GetResult();
                        updatedCount++;
                    }
                    else
                    {
                        // Field does not exist, create it using POST
                        var endpoint = $"{_httpClient.BaseAddress}/MetadataFields";
                        response = _httpClient.PostAsync(endpoint, content).GetAwaiter().GetResult();
                        createdCount++;
                    }

                    response.EnsureSuccessStatusCode();

                    // Deserialize the response to get the KeyfactorMetadataFieldId
                    var responseJson = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    var responseField = JsonSerializer.Deserialize<KeyfactorMetadataField>(responseJson, _jsonOptions);

                    if (responseField != null)
                    {
                        field.KeyfactorMetadataFieldId =
                            responseField.Id; // Update the UnifiedFormatField with the returned ID
                        _logger.Trace(
                            $"Field '{field.KeyfactorMetadataFieldName}' updated with KeyfactorMetadataFieldId: {field.KeyfactorMetadataFieldId}");
                    }
                }
                catch (Exception ex)
                {
                    // Log errors into error logs
                    _logger.Error(ex, $"Error processing metadata field: {field.KeyfactorMetadataFieldName}");
                }

            // Log counts of created and updated fields into info logs
            _logger.Info($"Metadata fields processed: {createdCount} created, {updatedCount} updated.");
        }

        /// <summary>
        ///     Retrieves a list of certificates from Keyfactor where the issuer DN contains the specified substring.
        ///     Optionally includes revoked and expired certificates, and includes metadata.
        /// </summary>
        /// <param name="issuerSubstring">The substring to search for in the issuer DN (default: "Sectigo").</param>
        /// <param name="includeRevokedAndExpired">Whether to include revoked and expired certificates (default: false).</param>
        /// <returns>A list of certificates matching the criteria, including metadata.</returns>
        public List<KeyfactorCertificate> GetCertificatesByIssuer(string issuerSubstring = "Sectigo",
            bool includeRevokedAndExpired = false)
        {
            if (string.IsNullOrEmpty(issuerSubstring))
                throw new ArgumentException("Issuer substring cannot be null or empty.", nameof(issuerSubstring));

            // Construct the query string in a readable format
            var queryString = $"IssuerDN -contains \"{issuerSubstring}\"";

            // Encode the query string for safe transmission
            var encodedQueryString = Uri.EscapeDataString(queryString);

            // Build the full query parameters
            var queryParameters = new StringBuilder($"QueryString={encodedQueryString}&includeMetadata=true");
            if (includeRevokedAndExpired) queryParameters.Append("&IncludeRevoked=true&IncludeExpired=true");

            // Construct the endpoint URL
            var endpoint = $"{_httpClient.BaseAddress}/Certificates?{queryParameters}";

            // Send the GET request
            var response = _httpClient.GetAsync(endpoint).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();

            // Deserialize the response JSON
            var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            // Log raw JSON response into trace logs
            _logger.Trace($"Raw JSON Response from GetCertificatesByIssuer: {json}");

            try
            {
                return JsonSerializer.Deserialize<List<KeyfactorCertificate>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                }) ?? new List<KeyfactorCertificate>();
            }
            catch (JsonException ex)
            {
                // Log errors into error logs
                _logger.Error(ex, "Failed to deserialize the certificate list from Keyfactor API.");
                throw new InvalidOperationException("Failed to deserialize the certificate list from Keyfactor API.",
                    ex);
            }
        }

        /// <summary>
        ///     Retrieves a paginated list of certificates from Keyfactor where the issuer DN contains the specified substring.
        ///     Optionally includes revoked and expired certificates, and includes metadata.
        /// </summary>
        /// <param name="issuerSubstring">The substring to search for in the issuer DN (default: "Sectigo").</param>
        /// <param name="includeRevokedAndExpired">Whether to include revoked and expired certificates (default: false).</param>
        /// <param name="pageNumber">The page number to retrieve (default: 1).</param>
        /// <param name="pageSize">The number of certificates per page (default: 100).</param>
        /// <returns>A list of certificates matching the criteria, including metadata.</returns>
        public List<KeyfactorCertificate> GetCertificatesByIssuer(string issuerSubstring = "Sectigo",
            bool includeRevokedAndExpired = false, int pageNumber = 1, int pageSize = 100)
        {
            if (string.IsNullOrEmpty(issuerSubstring))
                throw new ArgumentException("Issuer substring cannot be null or empty.", nameof(issuerSubstring));

            // Construct the query string in a readable format
            var queryString = $"IssuerDN -contains \"{issuerSubstring}\"";

            // Encode the query string for safe transmission
            var encodedQueryString = Uri.EscapeDataString(queryString);

            // Build the full query parameters
            var queryParameters = new StringBuilder($"QueryString={encodedQueryString}&includeMetadata=true");
            queryParameters.Append($"&PageReturned={pageNumber}&ReturnLimit={pageSize}");
            if (includeRevokedAndExpired) queryParameters.Append("&IncludeRevoked=true&IncludeExpired=true");

            // Construct the endpoint URL
            var endpoint = $"{_httpClient.BaseAddress}/Certificates?{queryParameters}";

            // Send the GET request
            var response = _httpClient.GetAsync(endpoint).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();

            // Deserialize the response JSON
            var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            // Log raw JSON response into trace logs
            _logger.Trace($"Raw JSON Response from GetCertificatesByIssuer: {json}");

            try
            {
                return JsonSerializer.Deserialize<List<KeyfactorCertificate>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                }) ?? new List<KeyfactorCertificate>();
            }
            catch (JsonException ex)
            {
                // Log errors into error logs
                _logger.Error(ex, "Failed to deserialize the certificate list from Keyfactor API.");
                throw new InvalidOperationException("Failed to deserialize the certificate list from Keyfactor API.",
                    ex);
            }
        }

        /// <summary>
        ///     Updates metadata for a given certificate in Keyfactor.
        /// </summary>
        /// <param name="certificateId">The ID of the certificate to update.</param>
        /// <param name="metadata">A dictionary containing the metadata key-value pairs to update.</param>
        /// <returns>True if the update was successful, otherwise false.</returns>
        public bool UpdateCertificateMetadata(int certificateId, Dictionary<string, string> metadata)
        {
            if (certificateId <= 0)
                throw new ArgumentException("Certificate ID must be greater than zero.", nameof(certificateId));

            if (metadata == null || metadata.Count == 0)
                throw new ArgumentException("Metadata cannot be null or empty.", nameof(metadata));

            // Construct the endpoint URL
            var endpoint = $"{_httpClient.BaseAddress}/Certificates/Metadata";

            // Create the request body
            var requestBody = new
            {
                Id = certificateId,
                Metadata = metadata
            };

            // Serialize the request body to JSON
            var jsonContent = JsonSerializer.Serialize(requestBody, _jsonOptions);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            // Log the JSON payload being sent
            _logger.Trace($"Sending JSON Payload to update metadata: {jsonContent}");

            try
            {
                // Send the PUT request
                var response = _httpClient.PutAsync(endpoint, content).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();

                return true; // Indicate success
            }
            catch (Exception ex)
            {
                // Log errors into error logs
                _logger.Error(ex, $"Failed to update metadata for certificate ID: {certificateId}");
                return false; // Indicate failure
            }
        }
    }
}

/// <summary>
///     Extension methods for registering Keyfactor clients in the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Registers a typed KeyfactorMetadataClient with IHttpClientFactory.
    /// </summary>
    public static IServiceCollection AddKeyfactorMetadataClient(
        this IServiceCollection services,
        string baseAddress)
    {
        services.AddHttpClient<KeyfactorMetadataClient>(client =>
        {
            client.BaseAddress = new Uri(baseAddress);
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        });
        return services;
    }

    public static IServiceCollection AddSectigoCustomFieldsClient(
        this IServiceCollection services,
        string baseAddress)
    {
        services.AddHttpClient<SectigoCustomFieldsClient>(client =>
        {
            client.BaseAddress = new Uri(baseAddress);
            // other defaults like Accept headers can be configured here
        });
        return services;
    }
}