// Copyright 2021 Keyfactor
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
                throw new ArgumentException("No configuration mode provided. Please specify KFtoSC or SCtoKF.");

            // Parse the config mode from the command-line arguments
            if (!Enum.TryParse(args[0], true, out configMode))
            {
                _logger.Error("Invalid configuration mode. Please specify KFtoSC or SCtoKF.");
                throw new ArgumentException("Invalid configuration mode. Please specify KFtoSC or SCtoKF.");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Unable to process tool mode: {ex.Message}");
            throw; // Use 'throw;' to preserve the original stack trace
        }

        _logger.Info($"Configuration mode set to: {configMode}");


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
            throw; // Use 'throw;' to preserve the original stack trace
        }

        Config settings = new();
        var manualFields = new List<ManualField>();
        var customFields = new List<CustomField>();
        List<CharDBItem> bannedCharList = new();

        try
        {
            // Bind config to Config class
            settings = config.GetSection("Config")
                           .Get<Config>()
                       ?? throw new InvalidOperationException("Missing config section in the config json file.");
            _ = config.GetSection("ManualFields")
                    .Get<List<ManualField>>()
                ?? throw new InvalidOperationException(
                    "Missing manual fields section in the fields json file.");
            _ = config.GetSection("CustomFields")
                    .Get<List<CustomField>>()
                ?? throw new InvalidOperationException(
                    "Missing custom fields section in the fields json file.");
            bannedCharList = config.GetSection("BannedCharacters")
                    .Get<List<CharDBItem>>() ?? new List<CharDBItem>()
                ?? throw new InvalidOperationException("Missing banned characters section in the config json file.");
        }
        catch (Exception ex)
        {
            _logger.Error($"Unable to process config file: {ex.Message}");
            throw; // Use 'throw;' to preserve the original stack trace
        }


        _logger.Info("Configuration loaded successfully. Testing connection to Sectigo API and Keyfactor API.");

        // Setup the service
        var services = new ServiceCollection();
        services.AddSectigoCustomFieldsClient(settings.sectigoAPIUrl);
        services.AddKeyfactorMetadataClient(settings.keyfactorAPIUrl);

        // Build the service provider
        var provider = services.BuildServiceProvider();

        // Test Sectigo connection
        var scClient = provider.GetRequiredService<SectigoCustomFieldsClient>();
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

        // Authenticate
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
                unifiedFieldList = config.GetSection("CustomFields").Get<List<UnifiedFormatField>>() ??
                                   new List<UnifiedFormatField>();
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
        var cumulativePartiallyProcessedCerts = new List<string>();
        var cumulativeSuccessfullyUpdatedCerts = new List<string>();

        // Initialize a list to collect certificates with missing custom fields
        var cumulativeMissingCustomFields = new List<string>();

        _logger.Info("Starting paginated retrieval of certificates from Keyfactor.");
        // This list only contains a Sectigo Cert Serial and a Sectigo ID to get extra details.
        var sectigoCertsDB = scClient.GetCertificatesByProfileId(settings.sslTypeIds,
            settings.syncRevokedAndExpiredCerts, settings.sectigoPageSize);

        // SCtoKF sync must be run at least once before KFtoSC sync can be run.
        // Retrieve certificates page by page
        while (hasMorePages)
        {
            // Get the current page of certificates
            var certsPage = kfClient.GetCertificatesByIssuer(settings.issuerDNLookupTerm,
                settings.syncRevokedAndExpiredCerts, pageNumber, pageSize);

            if (certsPage.Count > 0)
            {
                _logger.Debug(
                    $"[PAGE INFO] Retrieved {certsPage.Count} certificates on page {pageNumber}. Processing batch.");
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
                        var keyfactorMetadataPayload = new Dictionary<string, string>();

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

                                    if (localCustomField != null)
                                        keyfactorMetadataPayload[field.KeyfactorMetadataFieldName] =
                                            localCustomField.Value;
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
                            kfClient.UpdateCertificateMetadata(localKfCert.Id, keyfactorMetadataPayload);
                            cumulativeSuccessfullyUpdatedCerts.Add(localScCert.SerialNumber);
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

                        var hasPartialProcessing = false;

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
                                        if (field.KeyfactorDataType.Equals((int)MetadataDataType.Date))
                                        {
                                            // Define the expected date format
                                            var dateFormat = settings.keyfactorDateFormat;

                                            if (DateTime.TryParseExact(localCustomField.Value, dateFormat, null,
                                                    DateTimeStyles.None, out var parsedDate))
                                            {
                                                var formattedDate = parsedDate.ToString("yyyy-MM-dd");
                                                sectigoDataPayload.Add(new CustomFieldDetails
                                                {
                                                    Name = field.SectigoFieldName,
                                                    Value = formattedDate
                                                });
                                            }
                                            else
                                            {
                                                _logger.Warn(
                                                    $"Invalid date format for field {field.KeyfactorMetadataFieldName}. Expected format: {dateFormat}. Date received: {localCustomField.Value}. Date parsed: {parsedDate}.");
                                            }
                                        }
                                        else
                                        {
                                            sectigoDataPayload.Add(new CustomFieldDetails
                                            {
                                                Name = field.SectigoFieldName,
                                                Value = localCustomField.Value
                                            });
                                        }
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
                    throw new ArgumentException("Invalid configuration mode. Please specify KFtoSC or SCtoKF.");

                _logger.Info($"[PAGE PROCESSING] Processed page {pageNumber - 1}.");
            }
            else
            {
                hasMorePages = false; // No more certificates to retrieve
            }
        }

        // Log cumulative results before the application finishes
        _logger.Info(
            $"[SUMMARY] Completed retrieval and processing of certificates. Total certificates processed successfully: {totalCertsProcessed}. Certs without Custom Fields: {certsWithoutCustomFields}");
        if (cumulativePartiallyProcessedCerts.Count + cumulativeUnmatchedCerts.Count > 0)
            _logger.Warn(
                $"[SUMMARY] Total certificates with partial processing or errors: {cumulativePartiallyProcessedCerts.Count + cumulativeUnmatchedCerts.Count}");
        if (cumulativeUnmatchedCerts.Any())
            _logger.Warn(
                $"[SUMMARY] No matching Sectigo certificates found for the following Keyfactor certs: {string.Join(", ", cumulativeUnmatchedCerts)}");
        if (cumulativePartiallyProcessedCerts.Any())
            _logger.Warn(
                $"[SUMMARY] Following certificates were only partially processed: {string.Join(", ", cumulativePartiallyProcessedCerts)}");
        if (cumulativeSuccessfullyUpdatedCerts.Any())
            _logger.Debug(
                $"[SUMMARY] Successfully updated metadata for the following certificates: {string.Join(", ", cumulativeSuccessfullyUpdatedCerts)}");
        // Log aggregated warnings for missing custom fields during SCtoKF sync
        if (cumulativeMissingCustomFields.Any())
        {
            _logger.Info(
                $"[SUMMARY] No Metadata found for {cumulativeMissingCustomFields.Count} Sectigo certificates in Keyfactor)");
            _logger.Debug(
                $"[SUMMARY] No Metadata found for the following Sectigo certificates in Keyfactor: {string.Join(", ", cumulativeMissingCustomFields)}");
        }

        // End of the run
        _logger.Info("============================================================");
        _logger.Info($"[END] Sectigo Metadata Sync - Run completed at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        _logger.Info($"[RUN ID: {runId}]");
        _logger.Info("============================================================");
    }
}