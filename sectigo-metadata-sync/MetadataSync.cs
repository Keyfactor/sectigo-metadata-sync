// Copyright 2025 Keyfactor
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
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using NLog.Config;
using SectigoMetadataSync.Client;
using SectigoMetadataSync.Logic;
using SectigoMetadataSync.Models;

namespace SectigoMetadataSync;

internal class MetadataSync
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private static void Main(string[] args)
    {
        // Define the config directory path
        var configDirectory = Path.Combine(Directory.GetCurrentDirectory(), "config");

        // Ensure the config directory exists
        if (!Directory.Exists(configDirectory)) Directory.CreateDirectory(configDirectory);
        // Set up NLog to load the configuration from the config folder
        var nlogConfigPath = Path.Combine(configDirectory, "nlog.config");
        if (File.Exists(nlogConfigPath))
            LogManager.Configuration = new XmlLoggingConfiguration(nlogConfigPath);
        else
            _logger.Error($"NLog configuration file not found at {nlogConfigPath}. Using default configuration.");

        // Start of the run
        var runId = Guid.NewGuid();
        _logger.Info("============================================================");
        _logger.Info($"[START] Sectigo Metadata Sync - Run at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        _logger.Info($"[RUN ID: {runId}]");
        _logger.Info("============================================================");

        ///////////////////////////
        // SECTION I: Initial setup and connection testing
        _logger.Debug("Loading configuration.");

        ConfigMode configMode;
        try
        {
            if (args.Length == 0)
                throw new ArgumentException("No sync mode provided. Please specify either KFtoSC or SCtoKF using a command line argument.");

            // Parse the config mode from the command-line arguments
            if (!Enum.TryParse(args[0], true, out configMode))
            {
                _logger.Error("Invalid sync mode. Please specify KFtoSC or SCtoKF using a command line argument.");
                throw new ArgumentException("Invalid sync mode. Please specify KFtoSC or SCtoKF using a command line argument.");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Unable to process tool mode: {ex.Message}");
            throw; // Use 'throw;' to preserve the original stack trace
        }

        _logger.Info($"Tool sync mode set to: {configMode}");


        // Build the config
        var config = new ConfigurationBuilder().Build();
        try
        {
            config = new ConfigurationBuilder()
                .SetBasePath(configDirectory) // Set the base path to the config directory
                .AddJsonFile("config.json", false, false)
                .AddJsonFile("fields.json", false, false)
                .AddJsonFile("bannedcharacters.json", false, false)
                .Build();
        }
        catch (Exception ex)
        {
            _logger.Error($"Unable to load config file: {ex.Message}");
            throw; // preserve stack
        }

        Config settings = new();
        List<CharDBItem> bannedCharList = new();

        try
        {
            // Bind config to Config class (supports "Config" or "config" root)
            var rootSection = Helpers.GetRootConfigSection(config);
            settings = rootSection.Get<Config>()
                       ?? throw new InvalidOperationException("Missing 'config' section in config.json.");

            // Compute the bool *without* throwing if OAuth block is missing/empty
            settings.UseKeyfactorOAuth = Helpers.IsOAuthBlockUsable(settings.KeyfactorOAuth);

            // Bind other sections (keep your strictness)
            _ = config.GetSection("ManualFields")
                    .Get<List<UnifiedFormatField>>(o => o.ErrorOnUnknownConfiguration = true)
                ?? new List<UnifiedFormatField>();

            _ = config.GetSection("CustomFields")
                    .Get<List<UnifiedFormatField>>(o => o.ErrorOnUnknownConfiguration = true)
                ?? new List<UnifiedFormatField>();

            bannedCharList = config.GetSection("BannedCharacters")
                                 .Get<List<CharDBItem>>(o => o.ErrorOnUnknownConfiguration = true)
                             ?? new List<CharDBItem>();
        }
        catch (Exception ex)
        {
            _logger.Error($"Unable to process config file: {ex.Message}");
            throw;
        }

        // Parsing date from loaded config
        var tz = TimeZoneInfo.Local;
        settings.KeyfactorAddedSinceUtc = DateParser.ParseToUtcOrNull(
            settings.keyfactorAddedSince,
            tz,
            settings.keyfactorDateFormat);
        // warn if user supplied a value that couldn't be parsed
        if (!string.IsNullOrWhiteSpace(settings.keyfactorAddedSince) && settings.KeyfactorAddedSinceUtc is null)
            _logger.Warn($"Could not parse keyfactorAddedSince='{settings.keyfactorAddedSince}'. " +
                         $"Acceptable examples: '2025-09-01', '2025-09-01T13:45', '2025-09-01T13:45:00Z', " +
                         $"or matching keyfactorDateFormat='{settings.keyfactorDateFormat}'.");
        if (settings.KeyfactorAddedSinceUtc != null)
            _logger.Info($"Only syncing data for certs imported after {settings.KeyfactorAddedSinceUtc.ToString()}");

        _logger.Info("Configuration loaded successfully. Testing connection to Sectigo API and Keyfactor API.");
        ValueCoercion.KeyfactorDateFormat = settings.keyfactorDateFormat;

        if (settings.enableTruncation)
        {
            _logger.Info("IMPORTANT: Value truncation is enabled for this sync. Data will be truncated to fit Keyfactor/Sectigo character length limits.");
        }
        else
        {
            _logger.Info("IMPORTANT: Value truncation is disabled for this sync. Fields containing data that exceeds Keyfactor character length limits will not be synced.");
        } 
        ValueCoercion.EnableTruncation = settings.enableTruncation;
        ValueCoercionSC.EnableTruncation = settings.enableTruncation;
        // Setup the service
        var services = new ServiceCollection();
        services.AddSectigoClient(settings.sectigoAPIUrl);

        if (settings.UseKeyfactorOAuth)
        {
            _logger.Info("Utilizing OAuth for Authentication to Keyfactor API.");
            services.AddKeyfactorMetadataClientOAuth(
                settings.keyfactorAPIUrl,
                new OAuthServiceCollectionExtensions.OAuthOptions
                {
                    TokenUrl = settings.KeyfactorOAuth.TokenUrl,
                    ClientId = settings.KeyfactorOAuth.ClientId,
                    ClientSecret = settings.KeyfactorOAuth.ClientSecret,
                    ScopesCsv = settings.KeyfactorOAuth.ScopesCsv,
                    Audience = settings.KeyfactorOAuth.Audience,
                    refreshSkewSeconds = settings.KeyfactorOAuth!.RefreshSkewSeconds
                },
                string.IsNullOrWhiteSpace(settings.KeyfactorOAuth.RequestedWith)
                    ? "APIClient"
                    : settings.KeyfactorOAuth.RequestedWith
            );
        }
        else
        {
            _logger.Info("Utilizing Basic Auth for Authentication to Keyfactor API.");
            // Fall back to Basic/Windows (your existing flow)
            services.AddKeyfactorMetadataClient(settings.keyfactorAPIUrl);
        }

        // Build the service provider
        var provider = services.BuildServiceProvider();

        // Test Sectigo connection
        var scClient = provider.GetRequiredService<SectigoClient>();
        scClient.Authenticate(
            settings.sectigoLogin,
            settings.sectigoPassword,
            settings.sectigoCustomerUri
        );
        var scFields = new List<SectigoCustomField>();
        try
        {
            scFields = scClient.ListCustomFields();
            _logger.Debug("Retrieved Custom Fields from Sectigo.");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to connect to Sectigo API: {ex.Message}");
            _logger.Fatal($"Critical error: {ex.Message}");
            Environment.Exit(1); // Exit with a non-zero code to indicate failure
            throw; // Use 'throw;' to preserve the original stack trace
        }

        // Test Keyfactor connection
        var kfClient = provider.GetRequiredService<KeyfactorMetadataClient>();

        // Authenticate if not using oauth
        if (!settings.UseKeyfactorOAuth)
            kfClient.Authenticate(
                settings.keyfactorLogin,
                settings.keyfactorPassword
            );

        var kfFields = new List<KeyfactorMetadataField>();
        try
        {
            kfFields = kfClient.ListMetadataFields();
            _logger.Debug("Retrieved All Metadata Fields from Keyfactor.");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to connect to Keyfactor API: {ex.Message}");
            _logger.Fatal($"Critical error: {ex.Message}");
            Environment.Exit(1); // Exit with a non-zero code to indicate failure
            throw; // Use 'throw;' to preserve the original stack trace
        }

        _logger.Info("Connection tests completed. " +
                     "Proceeding to field overlap determination.");

        /////////////
        //SECTION II: Determination of field overlap
        var unifiedFieldList = new List<UnifiedFormatField>();
        try
        {
            // If auto-importing all custom fields is toggled:
            if (settings.importAllCustomFields)
            {
                _logger.Info("importAllCustomFields is enabled. Mapping Sectigo custom fields to UnifiedFormatField.");

                unifiedFieldList = scFields
                    .Where(scLocalField =>
                        settings.enableDisabledFieldSync || // Include all fields if enableDisabledFieldSync is true
                        !scLocalField.State.Equals("disabled",
                            StringComparison.OrdinalIgnoreCase)) // Exclude disabled fields otherwise
                    .Select(scLocalField => new UnifiedFormatField
                    {
                        SectigoFieldName = scLocalField.Name,
                        KeyfactorMetadataFieldName = scLocalField.Name, // Default to the same name
                        KeyfactorDescription = scLocalField.Name, // Default to the same name
                        KeyfactorDataType = (int)Helpers.ToKeyfactorDataType(scLocalField.Input.Type),
                        KeyfactorHint = scLocalField.Input.Type.ToString(),
                        KeyfactorValidation = scLocalField.Input.Type == CustomFieldInputType.TEXT_SINGLE_LINE
                            ? ".*" // Example: Add a default validation regex for single-line text
                            : null,
                        KeyfactorEnrollment =
                            scLocalField.Mandatories.Contains("ENROLLMENT") ? 1 : 0, // Map mandatories to enrollment
                        KeyfactorMessage = scLocalField.Input.Type == CustomFieldInputType.TEXT_SINGLE_LINE
                            ? "Please enter valid data."
                            : null,
                        KeyfactorOptions = scLocalField.Input.Type == CustomFieldInputType.TEXT_OPTION
                            ? scLocalField.Input.Options?.ToArray()
                            : null, // Map options for TEXT_OPTION fields
                        KeyfactorDefaultValue = null, // Default to null
                        KeyfactorDisplayOrder = 0, // Default to 0
                        KeyfactorCaseSensitive = false, // Default to false
                        KeyfactorMetadataFieldId = 0,
                        ToolFieldType = UnifiedFieldType.Custom
                    })
                    .ToList();

                _logger.Info($"Loaded {unifiedFieldList.Count} custom fields from Sectigo.");
            }
            else
            {
                _logger.Info("importAllCustomFields is disabled. Using field mapping.");
                // This loads custom metadata using the manualfields config.
                // Converts blank fields etc and preps the data.
                unifiedFieldList = config
                    .GetSection("CustomFields")
                    .Get<List<UnifiedFormatField>>(o => o.ErrorOnUnknownConfiguration = true);
                foreach (var item in unifiedFieldList) item.ToolFieldType = UnifiedFieldType.Custom;
            }
        }
        catch (InvalidOperationException ex)
        {
            _logger.Fatal($"Critical error: {ex.Message}");
            Environment.Exit(1); // Exit with a non-zero code to indicate failure
        }
        catch (Exception ex)
        {
            _logger.Error($"Error processing custom fields: {ex.Message}");
        }

        _logger.Debug($"Loaded {unifiedFieldList.Count.ToString()} Custom Fields.");

        // Load the manual fields from the config file and add it to the field list.
        var unifiedManualFieldList = config.GetSection("ManualFields")
            .Get<List<UnifiedFormatField>>()?
            .Select(item =>
            {
                item.ToolFieldType = UnifiedFieldType.Manual;
                return item;
            })
            .ToList() ?? new List<UnifiedFormatField>();
        unifiedFieldList.AddRange(unifiedManualFieldList);
        _logger.Debug($"Loaded {unifiedManualFieldList.Count.ToString()} Manual Fields.");

        // Initialize a list to collect invalid character details
        var invalidCharacterDetails = new List<string>();

        // Check both lists for bad characters, ask for restart if needed.
        var restartRequired = false;

        if (settings.importAllCustomFields)
            BannedCharacters.CheckForChars(unifiedFieldList, bannedCharList, invalidCharacterDetails);
        else
            BannedCharacters.CheckForChars(unifiedFieldList, bannedCharList, invalidCharacterDetails, true);

        foreach (var badchar in bannedCharList)
            if (badchar.replacementcharacter == "null")
                restartRequired = true;

        // Serialize the banned characters list with pretty-printing
        var formattedCharList = JsonSerializer.Serialize(new { BannedCharacters = bannedCharList },
            new JsonSerializerOptions
            {
                WriteIndented = true // Enable pretty-printing
            });

        File.WriteAllText(Path.Combine(configDirectory, "bannedcharacters.json"), formattedCharList);

        // Log aggregated invalid character details if replacements are missing
        if (restartRequired && invalidCharacterDetails.Any())
        {
            _logger.Warn("The following fields contain invalid characters with no replacements:");
            foreach (var detail in invalidCharacterDetails) _logger.Warn(detail);
        }


        if (restartRequired)
        {
            // Tool needs restarting at this point. 
            var bannedChars = new Exception("Replacement characters for auto-fill need specifying.");
            _logger.Fatal($"Critical error: {bannedChars.Message}");
            Environment.Exit(1); // Exit with a non-zero code to indicate failure
        }

        // Process the fields - run banned character replacement and send the fields off to Keyfactor.
        Parallel.ForEach(unifiedFieldList,
            field =>
            {
                field.KeyfactorMetadataFieldName =
                    BannedCharacters.ReplaceAllBannedCharacters(field.KeyfactorMetadataFieldName, bannedCharList);
            });
        kfClient.SendUnifiedMetadataFields(unifiedFieldList, kfFields);

        // Get list of all Sectigo Certs stored in Keyfactor.
        // Define pagination parameters
        var pageSize = settings.keyfactorPageSize;
        var pageNumber = 1;
        var hasMorePages = true;

        // Initialize counters and lists for tracking certificates
        var totalCertsProcessed = 0;
        var certsWithoutCustomFields = 0;

        // Initialize cumulative lists for unmatched and successfully updated certificates
        var cumulativeUnmatchedCerts = new List<string>();
        var unmatchedCount = 0;
        var cumulativePartiallyProcessedCerts = new List<string>();
        var partiallyProcessedCount = 0;
        var cumulativeSuccessfullyUpdatedCerts = new List<string>();
        var successfullyUpdatedCount = 0;

        // Initialize a list to collect certificates with missing custom fields
        var cumulativeMissingCustomFields = new List<string>();
        var missingCustomFields = 0;


        // Loading Sectigo metadata field IDs into the unified list (for custom fields only)
        foreach (var unifiedField in unifiedFieldList)
        {
            var matchingScField = scFields
                .FirstOrDefault(sc =>
                    string.Equals(sc.Name, unifiedField.SectigoFieldName,
                        StringComparison.OrdinalIgnoreCase));
            if (matchingScField != null)
            {
                unifiedField.SectigoMetadataFieldID = matchingScField.Id;
                unifiedField.SectigoCustomFieldType = matchingScField.Input.Type;
            }
        }

        _logger.Info("Retrieving base database of Sectigo Certs.");
        // This list only contains a Sectigo Cert Serial and a Sectigo ID to get extra details.
        var sectigoCertsDB = scClient.GetCertificatesByProfileId(settings.sslTypeIds,
            settings.syncRevokedAndExpiredCerts, settings.sectigoPageSize);

        // SCtoKF sync must be run at least once before KFtoSC sync can be run.
        // Retrieve certificates page by page
        while (hasMorePages)
        {
            // Get the current page of certificates
            var certsPage = kfClient.GetCertificatesByIssuer(settings.issuerDNLookupTerm,
                settings.syncRevokedAndExpiredCerts, pageNumber, pageSize,
                settings.KeyfactorAddedSinceUtc?.ToString("MM-dd-yyyy", CultureInfo.InvariantCulture));

            if (certsPage.Count > 0)
            {
                _logger.Info(
                    $"[PAGE INFO] Retrieved {certsPage.Count} certificates from Keyfactor on page {pageNumber}. Processing batch.");
                pageNumber++;

                // Process the current page of certificates
                if (configMode == ConfigMode.SCtoKF)
                    // For each cert in the current page
                    foreach (var localKfCert in certsPage)
                    {
                        // Strip leading zeros from the Keyfactor serial number
                        var strippedSerialNumber = localKfCert.SerialNumber.TrimStart('0');

                        // Find the matching Sectigo cert by serial number
                        var localScCert = sectigoCertsDB.FirstOrDefault(cert =>
                            cert.SerialNumber.Equals(strippedSerialNumber, StringComparison.OrdinalIgnoreCase));

                        if (localScCert == null)
                        {
                            cumulativeUnmatchedCerts.Add(strippedSerialNumber);
                            continue; // Skip to the next Keyfactor cert
                        }

                        // As we have the matched Sectigo ID, we now download the full Sectigo cert details.
                        var sectigoCertDetails = scClient.GetCertificateDetails(localScCert.SslId);

                        // Initialize a flag to track if any fields failed to process
                        var hasPartialProcessing = false;

                        // Now we process and prep the data for Keyfactor - first load manual fields.
                        var keyfactorMetadataPayload = new Dictionary<string, object>();

                        // Process manual fields
                        foreach (var field in unifiedFieldList.Where(f => f.ToolFieldType == UnifiedFieldType.Manual))
                            try
                            {
                                // Access the address using the reflection function helper
                                var result = Helpers.GetPropertyValue(sectigoCertDetails, field.SectigoFieldName)
                                    ?.ToString();
                                keyfactorMetadataPayload[field.KeyfactorMetadataFieldName] = result ?? string.Empty;
                            }
                            catch (Exception ex)
                            {
                                _logger.Warn(
                                    $"[PAGE ERROR] Error processing manual field '{field.KeyfactorMetadataFieldName}' for cert {localScCert.SerialNumber}: {ex.Message}");
                                hasPartialProcessing = true;
                            }

                        // Process custom fields
                        if (sectigoCertDetails.CustomFields != null && sectigoCertDetails.CustomFields.Count != 0)
                            foreach (var field in unifiedFieldList.Where(f =>
                                         f.ToolFieldType == UnifiedFieldType.Custom))
                                try
                                {
                                    // Find the custom field in SectigoCertificateDetails by SectigoFieldName
                                    var localCustomField = sectigoCertDetails.CustomFields?
                                        .FirstOrDefault(cf =>
                                            cf.Name.Equals(field.SectigoFieldName, StringComparison.OrdinalIgnoreCase));

                                    // Example when mapping a Sectigo custom field into a KF metadata field:
                                    var raw = localCustomField?.Value; // string
                                    using var doc =
                                        JsonDocument.Parse($"\"{raw?.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"");
                                    var coerced = ValueCoercion.Coerce(
                                        doc.RootElement,
                                        field.KeyfactorDataType,
                                        field.KeyfactorOptions
                                    );
                                    if (coerced is not null && !(coerced is string s && string.IsNullOrWhiteSpace(s)))
                                        keyfactorMetadataPayload[field.KeyfactorMetadataFieldName] = coerced;
                                }
                                catch (Exception ex)
                                {
                                    _logger.Warn(
                                        $"[PAGE ERROR] Error processing custom field '{field.KeyfactorMetadataFieldName}' for cert {localScCert.SerialNumber}: {ex.Message}");
                                    hasPartialProcessing = true;
                                }
                        else
                            certsWithoutCustomFields++;

                        // Update metadata in Keyfactor
                        try
                        {
                            if (keyfactorMetadataPayload.Count > 0)
                            {
                                kfClient.UpdateCertificateMetadata(localKfCert.Id,
                                    keyfactorMetadataPayload);
                                cumulativeSuccessfullyUpdatedCerts.Add(localScCert.SerialNumber);
                            }
                            else
                            {
                                _logger.Trace(
                                    $"Empty metadata payload for cert {localKfCert.SerialNumber}. Skipping upload.");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Warn(
                                $"[PAGE ERROR] Error updating metadata for cert {localScCert.SerialNumber}: {ex.Message}");
                            hasPartialProcessing = true;
                        }

                        // Update counters
                        if (hasPartialProcessing)
                            cumulativePartiallyProcessedCerts.Add(strippedSerialNumber);
                        else
                            totalCertsProcessed++;
                    }
                //Only syncs CUSTOM fields from Keyfactor to Sectigo, not MANUAL fields.
                else if (configMode == ConfigMode.KFtoSC)
                    foreach (var localKfCert in certsPage)
                    {
                        // Strip leading zeros from the Keyfactor serial number
                        var strippedSerialNumber = localKfCert.SerialNumber.TrimStart('0');
                        var hasPartialProcessing = false;
                        if (localKfCert.Metadata != null && localKfCert.Metadata.Count != 0)
                        {
                            // Find the matching Sectigo cert by serial number
                            var localScCert = sectigoCertsDB.FirstOrDefault(cert =>
                            cert.SerialNumber.Equals(strippedSerialNumber, StringComparison.OrdinalIgnoreCase));

                            if (localScCert == null)
                            {
                                cumulativeUnmatchedCerts.Add(strippedSerialNumber);
                                continue; // Skip to the next Keyfactor cert
                            }

                            // As we have the matched Sectigo ID, we now download the full Sectigo cert details.
                            var sectigoCertDetails = scClient.GetCertificateDetails(localScCert.SslId);


                            // Update the Sectigo certificate metadata
                            var sectigoDataPayload = new List<CustomFieldDetails>();

                            // Retrieve each existing Keyfactor metadata field
                            if (localKfCert.Metadata != null && localKfCert.Metadata.Count != 0)
                                foreach (var field in unifiedFieldList.Where(f =>
                                             f.ToolFieldType == UnifiedFieldType.Custom))
                                    try
                                    {
                                        // Find the custom field in SectigoCertificateDetails by SectigoFieldName
                                        var localCustomField = localKfCert.Metadata
                                            .FirstOrDefault(cf => cf.Key.Equals(field.KeyfactorMetadataFieldName,
                                                StringComparison.OrdinalIgnoreCase));
                                        if (!localCustomField.Equals(default(KeyValuePair<string, string>)))
                                        {
                                            var coerced = ValueCoercionSC.CoerceForSectigo(
                                                localCustomField.Value, field.SectigoCustomFieldType,
                                                field.KeyfactorOptions,
                                                settings.keyfactorDateFormat /* e.g., "M/d/yyyy h:mm:ss tt" */);
                                            if (!string.IsNullOrWhiteSpace(coerced))
                                                sectigoDataPayload.Add(new CustomFieldDetails
                                                {
                                                    Name = field.SectigoFieldName,
                                                    Value = coerced
                                                });
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.Warn(
                                            $"[PAGE ERROR] Error processing custom field '{field.KeyfactorMetadataFieldName}' for cert {localScCert.SerialNumber}: {ex.Message}");
                                        hasPartialProcessing = true;
                                    }

                            if (sectigoDataPayload.Count == 0)
                            {
                                certsWithoutCustomFields++;
                                cumulativeMissingCustomFields.Add(localScCert.SerialNumber);
                            }
                            else
                            {
                                // Update metadata in Sectigo
                                try
                                {
                                    scClient.UpdateCertificateMetadata(sectigoCertDetails.SslId, sectigoDataPayload,
                                        "update");
                                    totalCertsProcessed++; // Increment total processed count
                                    cumulativeSuccessfullyUpdatedCerts.Add(localScCert.SerialNumber);
                                }
                                catch (Exception ex)
                                {
                                    _logger.Warn(
                                        $"[PAGE ERROR] Error updating metadata for cert {localScCert.SerialNumber}: {ex.Message}");
                                    hasPartialProcessing = true;
                                }
                            }
                        }
                        else
                        {
                            _logger.Trace($"No custom fields data contained for certificate {localKfCert.SerialNumber}");
                            certsWithoutCustomFields++;
                            cumulativeMissingCustomFields.Add(localKfCert.SerialNumber);
                        }
                        // Update counters
                        if (hasPartialProcessing)
                            cumulativePartiallyProcessedCerts.Add(strippedSerialNumber);
                        else
                            totalCertsProcessed++;
                    }
                else
                    throw new ArgumentException("Invalid configuration mode. Please specify KFtoSC or SCtoKF.");

                // Flushing lists to avoid memory issues on large syncs
                cumulativeSuccessfullyUpdatedCerts.FlushRemainder(
                    _logger,
                    "SuccessfullyUpdated",
                    ref successfullyUpdatedCount
                );
                cumulativePartiallyProcessedCerts.FlushRemainder(
                    _logger,
                    "PartiallyProcessed",
                    ref partiallyProcessedCount
                );
                cumulativeUnmatchedCerts.FlushRemainder(
                    _logger,
                    "UnmatchedBetweenKfAndDc",
                    ref unmatchedCount
                );
                cumulativeMissingCustomFields.FlushRemainder(
                    _logger,
                    "MissingCustomFields",
                    ref missingCustomFields
                );
            }
            else
            {
                hasMorePages = false; // No more certificates to retrieve
            }
        }

        // Log cumulative results before the application finishes
        _logger.Info(
            $"[SUMMARY] Completed retrieval and processing of certificates. Total certificates processed successfully: {totalCertsProcessed}. Certs without Custom Fields data: {certsWithoutCustomFields}.");
        if (partiallyProcessedCount + unmatchedCount > 0)
            _logger.Warn(
                $"[SUMMARY] Total certificates with partial processing or errors: {partiallyProcessedCount + unmatchedCount}.");
        if (unmatchedCount > 0)
            _logger.Warn(
                $"[SUMMARY] No matching DigiCert certificates found for {unmatchedCount} Keyfactor certs.");
        // Log aggregated warnings for missing custom fields during SCtoKF sync
        if (missingCustomFields > 0)
            _logger.Info(
                $"[SUMMARY] No Metadata found for {missingCustomFields} DigiCert certificates in Keyfactor.");
        // End of the run
        _logger.Info("============================================================");
        _logger.Info($"[END] Sectigo Metadata Sync - Run completed at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        _logger.Info($"[RUN ID: {runId}]");
        _logger.Info("============================================================");
    }
}