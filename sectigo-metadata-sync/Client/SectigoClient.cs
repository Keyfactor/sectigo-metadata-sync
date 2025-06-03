using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NLog;
using SectigoMetadataSync.Models;

namespace SectigoMetadataSync.Client;

/// <summary>
///     Synchronous client for retrieving Sectigo custom fields (metadata) and SSL certificates.
///     Designed for use as a typed HttpClient via IHttpClientFactory.
/// </summary>
public class SectigoCustomFieldsClient
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger(); // NLog Logger

    private readonly HttpClient _httpClient;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    ///     Initializes a new instance of the SectigoCustomFieldsClient.
    ///     HttpClient is injected/configured via IHttpClientFactory; its BaseAddress should be set externally.
    /// </summary>
    public SectigoCustomFieldsClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <summary>
    ///     Configures API credentials for subsequent requests.
    ///     Must be called before invoking any list/get methods.
    ///     Uses header-based authentication: login, password, customerUri.
    /// </summary>
    public void Authenticate(string login, string password, string customerUri)
    {
        var headers = _httpClient.DefaultRequestHeaders;
        headers.Remove("login");
        headers.Remove("password");
        headers.Remove("customerUri");

        headers.Add("login", login);
        headers.Add("password", password);
        headers.Add("customerUri", customerUri);

        _logger.Info("Sectigo API authentication headers configured.");
    }

    /// <summary>
    ///     Lists all custom fields (full details). Maps to GET /api/customField/v2
    /// </summary>
    public List<SectigoCustomField> ListCustomFields()
    {
        _logger.Debug("Fetching all custom fields from Sectigo API.");
        var response = _httpClient.GetAsync("api/customField/v2").GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        return JsonSerializer.Deserialize<List<SectigoCustomField>>(json, _jsonOptions) ??
               new List<SectigoCustomField>();
    }

    /// <summary>
    ///     Lists custom fields filtered by certificate type. Maps to GET /api/customField/v2?certType={type}
    /// </summary>
    public List<SectigoCustomField> ListCustomFieldsByCertificateType(string certType)
    {
        _logger.Debug($"Fetching custom fields for certificate type: {certType}.");
        var uri = $"api/customField/v2?certType={Uri.EscapeDataString(certType)}";
        var response = _httpClient.GetAsync(uri).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        return JsonSerializer.Deserialize<List<SectigoCustomField>>(json, _jsonOptions) ??
               new List<SectigoCustomField>();
    }

    /// <summary>
    ///     Retrieves detailed information for a specific custom field by ID. Maps to GET /api/customField/v2/{id}
    /// </summary>
    public SectigoCustomField GetCustomFieldDetails(int id)
    {
        _logger.Debug($"Fetching details for custom field ID: {id}.");
        var uri = $"api/customField/v2/{id}";
        var response = _httpClient.GetAsync(uri).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        return JsonSerializer.Deserialize<SectigoCustomField>(json, _jsonOptions)
               ?? throw new InvalidOperationException(
                   $"Custom field with id {id} not found or failed to deserialize.");
    }

    /// <summary>
    ///     Retrieves a list of SSL certificates by sending individual requests for each profile ID.
    ///     If syncRevokedAndExpired is enabled, additional lookups are performed for Revoked and Expired statuses.
    /// </summary>
    /// <param name="profileIds">List of profile IDs to filter by.</param>
    /// <param name="syncRevokedAndExpired">Whether to include revoked and expired certificates.</param>
    /// <param name="sectigoPageSize">The size of each page for pagination.</param>
    /// <returns>A combined list of SSL certificates matching the profile IDs.</returns>
    public List<SectigoCertificate> GetCertificatesByProfileId(List<int> profileIds, bool syncRevokedAndExpired = false,
        int sectigoPageSize = 25)
    {
        if (profileIds == null || profileIds.Count == 0)
        {
            _logger.Debug("GetCertificatesByProfileId called with an empty or null profileIds list.");
            throw new ArgumentException("Profile IDs cannot be null or empty.", nameof(profileIds));
        }

        var combinedCertificates = new List<SectigoCertificate>();

        foreach (var profileId in profileIds)
        {
            _logger.Trace($"Processing profile ID: {profileId}");

            try
            {
                if (syncRevokedAndExpired)
                {
                    _logger.Trace(
                        $"SyncRevokedAndExpired is enabled. Fetching revoked and expired certificates for profile ID: {profileId}.");

                    // Fetch revoked certificates
                    _logger.Trace($"Fetching ALL certificates for profile ID: {profileId}.");
                    combinedCertificates.AddRange(
                        GetCertificatesByProfileIdAndStatus(profileId, null, sectigoPageSize));
                }
                else
                {
                    _logger.Trace($"Fetching active/Issued certificates for profile ID: {profileId}.");
                    combinedCertificates.AddRange(
                        GetCertificatesByProfileIdAndStatus(profileId, "Issued", sectigoPageSize));
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error processing profile ID: {profileId}");
            }
        }

        return combinedCertificates;
    }

    /// <summary>
    ///     Helper method to retrieve certificates for a specific profile ID and status with pagination.
    /// </summary>
    /// <param name="profileId">The profile ID to filter by.</param>
    /// <param name="status">The status to filter by (e.g., "Revoked", "Expired"). Pass null for active certificates.</param>
    /// <param name="sectigoPageSize">The size of each page for pagination.</param>
    /// <returns>A list of SSL certificates matching the profile ID and status.</returns>
    private List<SectigoCertificate> GetCertificatesByProfileIdAndStatus(int profileId, string? status,
        int sectigoPageSize = 25)
    {
        var pageSize = sectigoPageSize; // Define the size of each page
        var position = 0; // Start at the first entry
        var combinedCertificates = new List<SectigoCertificate>();

        while (true)
        {
            // Build the query string for the current profile ID, status, and pagination
            var queryString = $"sslTypeId={profileId}&position={position}&size={pageSize}";
            if (!string.IsNullOrEmpty(status)) queryString += $"&status={Uri.EscapeDataString(status)}";

            // Construct the endpoint URL
            var endpoint = $"api/ssl/v1?{queryString}";

            try
            {
                // Log the pagination details at debug level
                _logger.Trace(
                    $"Fetching certificates for profile ID {profileId} with status '{status ?? "Active"}'. Position: {position}, Page Size: {pageSize}");

                // Send the GET request
                var response = _httpClient.GetAsync(endpoint).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();

                // Deserialize the response JSON
                var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                var certificates = JsonSerializer.Deserialize<List<SectigoCertificate>>(json, _jsonOptions)
                                   ?? new List<SectigoCertificate>();

                // Log the number of certificates retrieved in the current page
                _logger.Trace(
                    $"Retrieved {certificates.Count} certificates for profile ID {profileId} with status '{status ?? "Active"}'.");

                // Add the retrieved certificates to the combined list
                combinedCertificates.AddRange(certificates);

                // If the number of certificates returned is less than the page size, we have reached the end
                if (certificates.Count < pageSize)
                {
                    _logger.Trace(
                        $"No more certificates to fetch for profile ID {profileId} with status '{status ?? "Active"}'.");
                    break;
                }

                // Increment the position for the next page
                position += pageSize;
            }
            catch (Exception ex)
            {
                // Log the error and return the certificates retrieved so far
                _logger.Error(ex,
                    $"Error retrieving certificates for profile ID {profileId} with status '{status ?? "Active"}'.");
                break;
            }
        }

        return combinedCertificates;
    }

    /// <summary>
    ///     Retrieves detailed information for a specific SSL certificate by its ID.
    /// </summary>
    /// <param name="sectigoCertId">The ID of the SSL certificate.</param>
    /// <returns>The detailed information of the SSL certificate.</returns>
    public SectigoCertificateDetails GetCertificateDetails(int sectigoCertId)
    {
        if (sectigoCertId <= 0)
            throw new ArgumentException("Certificate ID must be greater than zero.", nameof(sectigoCertId));

        // Construct the endpoint URL
        var endpoint = $"api/ssl/v1/{sectigoCertId}";

        // Send the GET request
        var response = _httpClient.GetAsync(endpoint).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        _logger.Trace("GET request successful. Status code: " + response.StatusCode);

        // Deserialize the response JSON
        var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        return JsonSerializer.Deserialize<SectigoCertificateDetails>(json, _jsonOptions)
               ?? throw new InvalidOperationException(
                   $"Certificate with ID {sectigoCertId} not found or failed to deserialize.");
    }

    /// <summary>
    ///     Updates metadata for a given SSL certificate by its ID.
    ///     Maps to PUT /api/ssl/v1
    /// </summary>
    /// <param name="sslId">The ID of the SSL certificate to update.</param>
    /// <param name="customFields">Custom fields to update (optional).</param>
    /// <param name="comments">Comments to update (optional).</param>
    /// <returns>The updated SSL certificate details.</returns>
    public SectigoCertificateDetails UpdateCertificateMetadata(
        int sslId,
        List<CustomFieldDetails>? customFields = null,
        string? comments = null)
    {
        if (sslId <= 0)
            throw new ArgumentException("Certificate ID must be greater than zero.", nameof(sslId));

        // Construct the request payload
        var payload = new
        {
            sslId,
            customFields,
            comments
        };

        // Serialize the payload to JSON
        var jsonPayload = JsonSerializer.Serialize(payload, _jsonOptions);
        _logger.Trace($"Constructed JSON payload for updating certificate metadata: {jsonPayload}");

        // Construct the endpoint URL
        var endpoint = "api/ssl/v1";

        // Send the PUT request
        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
        var response = _httpClient.PutAsync(endpoint, content).GetAwaiter().GetResult();

        // Log the response status
        _logger.Trace($"Received response with status code: {response.StatusCode}");
        response.EnsureSuccessStatusCode();

        // Deserialize the response JSON
        var jsonResponse = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        _logger.Trace($"Response JSON: {jsonResponse}");

        return JsonSerializer.Deserialize<SectigoCertificateDetails>(jsonResponse, _jsonOptions)
               ?? throw new InvalidOperationException("Failed to deserialize the updated certificate details.");
    }
}