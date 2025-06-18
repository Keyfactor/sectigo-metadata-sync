<h1 align="center" style="border-bottom: none">
    Sectigo Metadata Sync
</h1>

<p align="center">
  <!-- Badges -->
<img src="https://img.shields.io/badge/integration_status-pilot-3D1973?style=flat-square" alt="Integration Status: pilot" />
<a href="https://github.com/Keyfactor/sectigo-metadata-sync/releases"><img src="https://img.shields.io/github/v/release/Keyfactor/sectigo-metadata-sync?style=flat-square" alt="Release" /></a>
<img src="https://img.shields.io/github/issues/Keyfactor/sectigo-metadata-sync?style=flat-square" alt="Issues" />
<img src="https://img.shields.io/github/downloads/Keyfactor/sectigo-metadata-sync/total?style=flat-square&label=downloads&color=28B905" alt="GitHub Downloads (all assets, all releases)" />
</p>

<p align="center">
  <!-- TOC -->
  <a href="#support">
    <b>Support</b>
  </a> 
  ·
  <a href="#license">
    <b>License</b>
  </a>
  ·
  <a href="https://github.com/topics/keyfactor-integration">
    <b>Related Integrations</b>
  </a>
</p>

## Support
The Sectigo Metadata Sync is open source and there is **no SLA**. Keyfactor will address issues as resources become available. Keyfactor customers may request escalation by opening up a support ticket through their Keyfactor representative. 

> To report a problem or suggest a new feature, use the **[Issues](../../issues)** tab. If you want to contribute actual bug fixes or proposed enhancements, use the **[Pull requests](../../pulls)** tab.


## Overview

This tool automates the synchronization of metadata fields between Sectigo and Keyfactor. It performs two primary operations:

1. **SCtoKF** – Synchronizes custom fields and contained data, and additional requested data from Sectigo into Keyfactor. 
2. **KFtoSC** – Synchronizes custom field data from Keyfactor back to Sectigo. 

Fields listed in `fields.json` that do not already exist in Keyfactor will be created automatically.
> **Note:** Certificates must already be imported into Keyfactor for metadata synchronization to work. The tool does *not* import certificates themselves.

---

## Installation and Usage

1. **Prerequisites**
   * .NET 9 or newer runtime.
   * A valid Sectigo account with API access credentials.
   * A Keyfactor account with API access credentials.
   * The following config files filled in within the config sub-directory:

     * `config.json`
     * `fields.json`
     * `bannedcharacters.json`, which will be generated during the first run if needed.
    
   * The tool has been designed for Keyfactor 25.1, but was tested as compatible with older versions.

2. **Running The Tool**
   ```powershell
   SectigoMetadataSync.exe sctokf
   ```

   or

   ```powershell
   SectigoMetadataSync.exe kftosc
   ```
  > **Note:** sctokf sync must be run at least once before kftosc can be.

---

## Command Line Arguments

One of the following two modes must be specified as the **first (and only) argument** when launching the executable:

* `sctokf`
  Synchronizes custom and manual fields **from Sectigo into Keyfactor**.

  * Reads each entry in `fields.json`.
  * For each “ManualField,” extracts the specified data from Sectigo’s certificate details JSON.
  * For each “CustomField,” reads Sectigo’s custom-field value and writes it into Keyfactor.
  * The required metadata fields are created in Keyfactor if they do not already exist.

* `kftosc`
  Synchronizes custom fields (NOT manual fields) **from Keyfactor back into Sectigo**.

  * Reads each `CustomField` entry in `fields.json`.
  * For each field, retrieves the value from Keyfactor and updates Sectigo.

> **Note:** If no argument or an invalid argument is provided, the tool will log an error and exit.

---

## Settings

### 1. `config\config.json` Settings

* **sectigoLogin**
  Login name/email for Sectigo API (e.g., the account you use to log into Sectigo).

* **sectigoPassword**
  Password for the Sectigo account.

* **sectigoCustomerUri**
 This is a static value that determined the customer's account on the Certificate Platform. This can be found as part of the portal login URL `https://hard.cert-manager.com/customer/{CustomerUri}`

* **sectigoAPIUrl**
  Base URL for Sectigo’s API (e.g., `https://cert-manager.com`).

* **keyfactorLogin**
  Keyfactor domain and username, in the form `DOMAIN\\Username`.

* **keyfactorPassword**
  Password for the Keyfactor account.

* **keyfactorAPIUrl**
  Full Keyfactor API endpoint (e.g., `https://your-keyfactor-server.com/keyfactorapi`).

* **keyfactorDateFormat**
  Date/time format to use when reading Date information from Keyfactor for SCtoKF mode. Varies based on your Keyfactor Command version.
  Default for Keyfactor 25.1 is "M/d/yyyy h:mm:ss tt". Older versions may use "yyyy-MM-dd". 
  In case of errors, please review the logs to obtain the correct format.
    

* **importAllCustomFields**
  String `"true"` or `"false"`.

  * If `"true"`, on `SCtoKF` mode the tool will import *all* custom metadata fields that exist in Sectigo, and use autofill with banned characters replacement to convert field names.
  * If `"false"`, it will only import the ones explicitly listed under `"CustomFields"` in `fields.json`, and will not alter their names. The tool will still check for banned characters, and will
    generate an error if any are found.

* **syncRevokedAndExpiredCerts**
  String `"true"` or `"false"`.

  * If `"true"`, certificates with status “revoked” or “expired” in Sectigo and Keyfactor will also be included for metadata sync.
  * If `"false"`, those certificates are skipped.

* **issuerDNLookupTerm**
  A substring to match against the Issuer Distinguished Name, which is how the tool identifies Sectigo-issued certificates in Keyfactor.

  * Only certificates whose Issuer DN contains this term (e.g., `"Sectigo"`) will be considered.

* **enableDisabledFieldSync**
  String `"true"` or `"false"`.

  * If `"true"`, fields in Sectigo that are marked “disabled” will still be synchronized. In practice, the fields will be created in Keyfactor but not populated with data, as the data is not returned by Sectigo if the field is inactive.
  * If `"false"`, disabled fields are ignored.

* **sslTypeIds**
  Array of integers specifying which Sectigo SSL certificate Type IDs should be queried. (sslTypeIds). These would be the same used for the Keyfactor Gateway Configuration.
  Example: `[ 111111, 222222 ]` might correspond to “Domain Validated”, “Organization Validated”, etc.

  * Only certificates whose `sslType` matches one of these IDs are processed.

* **sectigoPageSize**
  Maximum number of certificates to fetch per page from Sectigo (pagination).
  Default in code is `25`.

* **keyfactorPageSize**
  Maximum number of certificates to fetch per page from Keyfactor (pagination).
  Default in code is `100`.

---

### 2. `config\fields.json` Settings
* **ManualFields and CustomFields**

    Manual Fields are used to import data from “static” Sectigo certificate attributes obtained using the Sectigo API "Get SSL certificate details" endpoint (e.g., serial number, common name) into Keyfactor. 
    To retrieve this data, you specify the path to the attribute you wish to retrieve in the sectigoFieldName, using `.` for separation. For example, to retrieve certificateDetails.issuer
    from certificateDetails, list certificateDetails.issuer as the sectigoFieldName, as issuer is a part of certificateDetails. 
    Review the Sectigo API documentation for the SSL "Get SSL certificate details" endpoint to view the available attributes and subattributes.

    Custm Fields are used to import data from Sectigo custom fields, which are user-defined fields. If importAllCustomFields is set to true, the tool will match the field types and other information contained in Sectigo when it creates the fields within Keyfactor.
    Otherwise, information listed in the CustomFields array within `fields.json` will be used to create the fields within Keyfactor.
    > **Note:**  The Keyfactor fields listed in ManualFields and KeyfactorFields are compatible with Keyfactor Command 25.1, 
    but the tool will work with older versions of Keyfactor and the unused fields and contained data will be ignored.

   
* **ManualFields**

  Each object must include:

  1. `sectigoFieldName`

     * The exact JSON path of the field in Sectigo’s “certificate details” response.
     * Use dot notation if nested (e.g., `'certificate.subject.organization'`).
  2. `keyfactorMetadataFieldName`

     * The desired metadata field name in Keyfactor. **Must contain only `[A–Za–z0–9-_]`.**
  3. `keyfactorDescription`

     * A short description for display in Keyfactor’s UI.
  4. `keyfactorDataType`

     * Integer code for Keyfactor’s data type:

       * `1` = String
       * `2` = Integer
       * `3` = Date
       * `4` = Boolean
       * `5` = MultipleChoice
       * `6` = BigText
       * `7` = Email
  5. `keyfactorHint`

     * Hint text shown in Keyfactor when entering or viewing data.
  6. `keyfactorValidation` (nullable)

     * A regular expression that the input must match (only for string fields).
  7. `keyfactorEnrollment`

     * How the field behaves during enrollment. Valid values depend on your Keyfactor setup (typically `0` = optional, `1` = required).
  8. `keyfactorMessage` (nullable)

     * Message shown if validation fails.
  9. `keyfactorOptions` (nullable)

     * Array of strings for multiple‐choice fields. Ignored if `keyfactorDataType` ≠ `5`.
  10. `keyfactorDefaultValue` (nullable)

      * Default value for the metadata field.
  11. `keyfactorDisplayOrder`

      * Integer specifying the display order in the Keyfactor administration view.
  12. `keyfactorCaseSensitive`

      * `true` or `false`. Only relevant if `keyfactorValidation` is provided for a string field.

* **CustomFields**
 Uses the same fields as above. An array of objects defining Sectigo *custom* fields to be synchronized. If `importAllCustomFields = true` (in `stock-config.json`), you may omit individual entries here and let the tool import all custom fields automatically.

---

### 3. config\bannedcharacters.json

On the very first run, the tool inspects all Sectigo custom field names and compares them against Keyfactor’s allowed metadata‐field naming rules (alphanumeric, `-`, and `_` only). If it finds unsupported characters, it will produce a file called `bannedCharacters.json` in the same directory, with entries like:

```jsonc
[
  {
    "id": 1,
    "character": " ",
    "replacementCharacter": null
  },
  {
    "id": 2,
    "character": "/",
    "replacementCharacter": null
  }
]
```

* **character**
  The unsupported character detected in the Sectigo field name.

* **replacementCharacter** (initially `null`)
  A value you must supply before rerunning. It should be any alphanumeric, `-`, or `_` string that the tool can use to replace the banned character.
  For example, to replace spaces (`" "`) with underscores (`"_"`), set `"replacementCharacter": "_"`.

If any `"replacementCharacter"` remains `null`, the tool will exit with an error on the next run. Once you populate all replacements, fields will be created in Keyfactor with names free of banned characters.

---

## Logging

Logging is managed via NLog and is configured in the accompanying `config\NLog.config` file. By default, logs are written to a `/logs` subdirectory under the folder containing the executable. There are two main log targets—one for all log levels and one that captures only errors and fatals.

### Logging Rules

```xml
<rules>
  <!-- All levels (DEBUG, INFO, WARN, ERROR, FATAL) go to MainLogFile -->
  <logger name="*" minlevel="Info" writeTo="MainLogFile" />

  <!-- Only ERROR and FATAL levels go to ErrorLogFile -->
  <logger name="*" minlevel="Error" writeTo="ErrorLogFile" />
</rules>
```

* **MainLogFile (`MetadataSync.log`)**
  Captures everything from `Debug` up to `Fatal`. This is the primary log for troubleshooting or auditing all operations.
  For debugging, set minlevel="Debug" or minlevel="Trace".

* **ErrorLogFile (`MetadataSync-Errors.log`)**
  Captures only `Error` and `Fatal` entries. Use this file to quickly locate failed operations without sifting through lower‐level debug or info messages.

---

#### Example Log Entry (MainLogFile)

```
2025-06-03 02:00:01.2345 | INFO  | SectigoMetadataSync.MetadataSync | Starting SCtoKF synchronization mode.
```

#### Example Log Entry (ErrorLogFile)

```
2025-06-03 02:05:42.9876 | ERROR | SectigoMetadataSync.SectigoClient | Failed to retrieve certificates: HTTP 401 Unauthorized.
```

---

**Tip:** If you need to change log levels or file paths, open `NLog.config` and edit:

* `<variable name="logDirectory" value="…"/>`
* `<variable name="logFileName" value="…"/>`
* `<variable name="errorFileName" value="…"/>`
* Or adjust the `<rules>` block to include/exclude other levels or targets.

Always restart the tool after modifying `NLog.config` to ensure changes take effect.

---

## Example Workflow

1. **Initial Setup**

   * Populate `config\config.json` with your Sectigo and Keyfactor API credentials.
   * Populate `config\fields.json` with the manual and/or custom fields you wish to sync.

2. **First Run (Detect Banned Characters)**

   ```powershell
   cd C:\Tools\SectigoSync\
   .\SectigoMetadataSync.exe SCtoKF
   ```

   * If Sectigo custom field names contain banned characters, you will see warnings in the log and the tool will exit.
   * A file named `bannedcharacters.json` will be created listing each banned character with `"replacementCharacter": null`.

3. **Populate Banned Characters**

   * Open `config\bannedcharacters.json`.
   * For each entry where `"replacementCharacter": null`, supply a valid replacement (alphanumeric, `-`, or `_`).

     ```jsonc
     [
       {
         "id": 1,
         "character": " ",
         "replacementCharacter": "_"
       },
       {
         "id": 2,
         "character": "/",
         "replacementCharacter": "-"
       }
     ]
     ```
   * Save the file.

4. **Second Run (Create Fields & Sync Data)**

   ```powershell
   .\SectigoMetadataSync.exe SCtoKF
   ```

   * The tool will now convert Sectigo custom‐field names (using your replacements), create new Keyfactor metadata fields if needed, and write all custom/manual data into Keyfactor.

---

### Troubleshooting

* **Missing or Invalid JSON**

  * If `stock-config.json` or `fields.json` is malformed or missing required sections, the tool logs an error and exits.
  * Ensure both files exist, are valid JSON, and contain the required properties (see “Settings” above).

* **Authentication Failures**

  * Double‐check `sectigoLogin`/`sectigoPassword` and `keyfactorLogin`/`keyfactorPassword`.
  * Ensure your API user has adequate permissions to create/update metadata fields.

* **Field Creation Errors**

  * If Keyfactor rejects a field name (e.g., still contains a banned character), verify that `bannedCharacters.json` is up to date.
  * If Sectigo rejects a custom‐field update, ensure you are using the correct custom‐field “name” as visible in Sectigo’s administration UI.
  * Consider deleting 'bannedCharacters.json' and re-running the tool to regenerate it from scratch.

---



## License

Apache License 2.0, see [LICENSE](LICENSE).

## Related Integrations

See all [Keyfactor integrations](https://github.com/topics/keyfactor-integration).