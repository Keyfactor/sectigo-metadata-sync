# Sectigo Metadata Sync Tool

## Overview

This tool automates the synchronization of metadata fields between Sectigo and Keyfactor. It performs two primary operations:

1. **SCtoKF** – Synchronizes both custom and manual (non-custom) fields from Sectigo into Keyfactor. Any fields listed in `fields.json` that do not already exist in Keyfactor will be created automatically.
2. **KFtoSC** – Synchronizes custom fields from Keyfactor back to Sectigo. Fields are created in Sectigo on demand if they do not exist.

This utility requires a working Sectigo API account, a Keyfactor API endpoint, and a correctly configured set of JSON files (`stock-config.json`, `fields.json`, and optionally a banned-characters file). Each time the executable runs, it will scan for new or modified fields and update the target system accordingly.

> **Note:** Certificates must already be imported into Keyfactor for metadata synchronization to work. The tool does *not* import certificates themselves.

---

## Installation and Usage

1. **Prerequisites**

   * Windows environment (supports scheduling via Task Scheduler).
   * .NET runtime that matches the build target of the executable.
   * A valid Sectigo account with API access credentials.
   * A Keyfactor account with API access credentials.
   * Network access from the machine running this tool to both the Sectigo API endpoint and the Keyfactor API endpoint.
   * The following files placed in a single directory alongside the executable:

     * `SectigoMetadataSync.exe` (the compiled tool)
     * `stock-config.json`
     * `fields.json`
     * *(Optional)* A banned-characters JSON file, if the first run reports unsupported characters.

2. **Directory Layout**

   ```
   C:\SomeFolder\SectigoMetadataSync\
   ├─ SectigoMetadataSync.exe
   ├─ stock-config.json
   ├─ fields.json
   └─ (bannedCharacters.json)    ← Optional; see “Banned Characters” below
   ```

3. **Running Manually**
   Open a Command Prompt, navigate to the directory above, and invoke:

   ```powershell
   SectigoMetadataSync.exe SCtoKF
   ```

   or

   ```powershell
   SectigoMetadataSync.exe KFtoSC
   ```

   The tool will:

   * Read `stock-config.json` and `fields.json`.
   * Connect to Sectigo and Keyfactor APIs.
   * Create any missing metadata fields.
   * Populate metadata fields on certificates according to the selected mode.

4. **Scheduling (Windows Task Scheduler)**
   To run this tool automatically (recommended interval: once per week):

   1. Open Task Scheduler.
   2. Create a new task with “Run whether user is logged on or not.”
   3. In **Actions**, point to `SectigoMetadataSync.exe` and add an argument (`SCtoKF` or `KFtoSC`).
   4. In **Triggers**, set the desired recurrence (e.g., weekly on Monday at 2:00 AM).
   5. Ensure the **Start in** field is set to the folder containing the executable and JSON files.
   6. Save with the appropriate credentials.

---

## Command Line Arguments

One of the following two modes must be specified as the **first (and only) argument** when launching the executable:

* `SCtoKF`
  Synchronizes custom and manual fields **from Sectigo into Keyfactor**.

  * Reads each entry in `fields.json`.
  * For each “ManualField,” extracts the specified data from Sectigo’s certificate JSON and writes it into Keyfactor.
  * For each “CustomField,” reads Sectigo’s custom-field value and writes it into Keyfactor.
  * If a field does not exist in Keyfactor, it is created on the fly.

* `KFtoSC`
  Synchronizes custom fields **from Keyfactor back into Sectigo**.

  * Reads each `CustomField` entry in `fields.json`.
  * For each field, retrieves the value from Keyfactor and updates Sectigo.
  * If the custom field does not exist in Sectigo, it is created first (using Sectigo’s API).

> If no argument or an invalid argument is provided, the tool will log an error and exit.

---

## Settings

### 1. `stock-config.json` Settings

```jsonc
{
  "config": {
    "sectigoLogin": "user@example.com",
    "sectigoPassword": "*********",
    "sectigoCustomerUri": "sectigo-customer",
    "sectigoAPIUrl": "https://api.sectigo.com",
    "keyfactorLogin": "DOMAIN\\Username",
    "keyfactorPassword": "********",
    "keyfactorAPIUrl": "https://your-keyfactor-server.com/keyfactorapi",
    "keyfactorDateFormat": "M/d/yyyy h:mm:ss tt",
    "importAllCustomFields": "false",
    "syncRevokedAndExpiredCerts": "true",
    "issuerDNLookupTerm": "Sectigo",
    "enableDisabledFieldSync": "false",
    "sslTypeIds": [ 111111, 222222 ],
    "sectigoPageSize": 25,
    "keyfactorPageSize": 100
  }
}
```

* **sectigoLogin**
  Login name/email for Sectigo API (e.g., the account you use to log into Sectigo).

* **sectigoPassword**
  Password for the Sectigo account.

* **sectigoCustomerUri**
  The “Customer URI” or customer identifier used in Sectigo API URIs.
  Example: if your certificates endpoint is `https://api.sectigo.com/cert-manager/v1/certificates/sectigo-customer`, then `sectigoCustomerUri` = `"sectigo-customer"`.

* **sectigoAPIUrl**
  Base URL for Sectigo’s API (e.g., `https://api.sectigo.com/cert-manager/v1`).

* **keyfactorLogin**
  Keyfactor domain and username, in the form `DOMAIN\Username`.

* **keyfactorPassword**
  Password for the Keyfactor account.

* **keyfactorAPIUrl**
  Full Keyfactor API endpoint (e.g., `https://your-keyfactor-server.com/keyfactorapi`).

* **keyfactorDateFormat**
  Date/time format to use when parsing or writing datetime fields in Keyfactor.
  Example: `"M/d/yyyy h:mm:ss tt"` (for `6/30/2025 11:45:00 AM`).

* **importAllCustomFields**
  String `"true"` or `"false"`.

  * If `"true"`, on `SCtoKF` mode the tool will import *all* custom metadata fields that exist in Sectigo.
  * If `"false"`, it will only import the ones explicitly listed under `"CustomFields"` in `fields.json`.

* **syncRevokedAndExpiredCerts**
  String `"true"` or `"false"`.

  * If `"true"`, certificates with status “revoked” or “expired” in Sectigo will also be included for metadata sync.
  * If `"false"`, those certificates are skipped.

* **issuerDNLookupTerm**
  A substring to match against the Issuer Distinguished Name.

  * Only certificates whose Issuer DN contains this term (e.g., `"Sectigo"`) will be considered.

* **enableDisabledFieldSync**
  String `"true"` or `"false"`.

  * If `"true"`, fields in Sectigo that are marked “disabled” will still be synchronized (both their schema and data).
  * If `"false"`, disabled fields are ignored.

* **sslTypeIds**
  Array of integers specifying which Sectigo SSL certificate Type IDs should be queried.
  Example: `[ 111111, 222222 ]` might correspond to “Domain Validated”, “Organization Validated”, etc.

  * Only certificates whose `sslType` matches one of these IDs are processed.

* **sectigoPageSize**
  Maximum number of certificates to fetch per page from Sectigo (pagination).
  Default in code is `25`; you may increase if you expect larger result sets.

* **keyfactorPageSize**
  Maximum number of certificates to fetch per page from Keyfactor (pagination).
  Default in code is `100`; you may increase if you expect larger result sets.

---

### 2. `fields.json` Settings

```jsonc
{
  "_comments": {
    "ManualFields": "List of fields used for loading static information from Sectigo as certificate metadata in Keyfactor.",
    "CustomFields": "List of custom metadata fields to be synced between Keyfactor and Sectigo.",
    "sectigoFieldName": "The name of the field in Sectigo. If nested in the certificate JSON, use dot notation, e.g. \"certificate.subject.organization\".",
    "keyfactorMetadataFieldName": "The name of the field in Keyfactor. Use only alphanumeric, '-' or '_' characters (no spaces).",
    "keyfactorDescription": "A description of the metadata field for display in Keyfactor.",
    "keyfactorDataType": "The data type of the field (1 = String, 2 = Integer, 3 = Date, 4 = Boolean, 5 = MultipleChoice, 6 = BigText, 7 = Email).",
    "keyfactorHint": "A short hint to guide users on what to enter in the field (Keyfactor UI).",
    "keyfactorValidation": "A regular expression to validate the field’s input (only for string fields).",
    "keyfactorEnrollment": "How the field is handled during certificate enrollment in Keyfactor (0 = Optional, 1 = Required, etc.).",
    "keyfactorMessage": "Validation failure message to display if the input does not match `keyfactorValidation`.",
    "keyfactorOptions": "Array of values for a multiple-choice field (ignored if not a MultipleChoice type).",
    "keyfactorDefaultValue": "Default value for the field (used if none is provided).",
    "keyfactorDisplayOrder": "Numeric order in which the field appears in Keyfactor’s UI.",
    "keyfactorCaseSensitive": "Boolean indicating if validation is case‐sensitive (only for string fields)."
  },
  "ManualFields": [
    {
      "sectigoFieldName": "certificate.serialNumber",
      "keyfactorMetadataFieldName": "SerialNumber",
      "keyfactorDescription": "Certificate serial number from Sectigo",
      "keyfactorDataType": 1,
      "keyfactorHint": "Automatically populated from Sectigo",
      "keyfactorValidation": null,
      "keyfactorEnrollment": 0,
      "keyfactorMessage": null,
      "keyfactorOptions": null,
      "keyfactorDefaultValue": null,
      "keyfactorDisplayOrder": 1,
      "keyfactorCaseSensitive": false
    },
    {
      "sectigoFieldName": "certificate.commonName",
      "keyfactorMetadataFieldName": "CommonName",
      "keyfactorDescription": "Certificate Common Name (CN)",
      "keyfactorDataType": 1,
      "keyfactorHint": "Automatically populated from Sectigo",
      "keyfactorValidation": null,
      "keyfactorEnrollment": 0,
      "keyfactorMessage": null,
      "keyfactorOptions": null,
      "keyfactorDefaultValue": null,
      "keyfactorDisplayOrder": 2,
      "keyfactorCaseSensitive": false
    }
    // … add additional ManualFields as needed
  ],
  "CustomFields": [
    {
      "sectigoFieldName": "DeviceLocation",
      "keyfactorMetadataFieldName": "DeviceLocation",
      "keyfactorDescription": "Location of the device (custom field)",
      "keyfactorDataType": 1,
      "keyfactorHint": "Enter where the device is located",
      "keyfactorValidation": null,
      "keyfactorEnrollment": 0,
      "keyfactorMessage": null,
      "keyfactorOptions": null,
      "keyfactorDefaultValue": null,
      "keyfactorDisplayOrder": 3,
      "keyfactorCaseSensitive": false
    },
    {
      "sectigoFieldName": "WarrantyExpiry",
      "keyfactorMetadataFieldName": "WarrantyExpiry",
      "keyfactorDescription": "Warranty expiration date for the device",
      "keyfactorDataType": 3,
      "keyfactorHint": "Format: MM/dd/yyyy",
      "keyfactorValidation": "\\d{2}/\\d{2}/\\d{4}",
      "keyfactorEnrollment": 0,
      "keyfactorMessage": "Date must be in MM/dd/yyyy format.",
      "keyfactorOptions": null,
      "keyfactorDefaultValue": null,
      "keyfactorDisplayOrder": 4,
      "keyfactorCaseSensitive": false
    }
    // … add additional CustomFields as needed
  ]
}
```

* **ManualFields**
  An array of objects defining “static” Sectigo certificate attributes (e.g., serial number, common name) to be imported into Keyfactor. Each object must include:

  1. `sectigoFieldName`

     * The exact JSON path of the field in Sectigo’s “certificate details” response.
     * Use dot notation if nested (e.g., `'certificate.subject.organization'`).
  2. `keyfactorMetadataFieldName`

     * The desired metadata field name in Keyfactor. Must contain only `[A–Za–z0–9-_]`.
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
  An array of objects defining Sectigo *custom* metadata fields to be synchronized. The property names are identical to those in `ManualFields`, but the tool will read/write against Sectigo’s “customFields” APIs rather than the static certificate JSON. If `importAllCustomFields = true` (in `stock-config.json`), you may omit individual entries here and let the tool import all custom fields automatically.

---

### 3. Banned Characters (Optional)

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

Logging is handled via NLog. By default, `NLog.config` is included alongside the executable. In this file you can set:

```xml
<variable name="minLogLevel" value="Info" />
```

Change `value="Info"` to one of:

* `"Debug"` — Verbose logs (development/troubleshooting).
* `"Trace"` — All internal steps (maximum verbosity).
* `"Info"` — High‐level progress messages (default).
* `"Warn"` — Warnings only.
* `"Error"` — Errors only.

Logs (by default) are written to:

* A console window.
* A rolling file named `SectigoMetadataSync.log` in the same directory.

You can adjust targets, layouts, and file paths in `NLog.config` as desired.

---

## Example Workflow

1. **Initial Setup**

   * Place `SectigoMetadataSync.exe`, `stock-config.json`, and `fields.json` in `C:\Tools\SectigoSync\`.
   * Populate `stock-config.json` with your Sectigo and Keyfactor API credentials.
   * Populate `fields.json` with the manual and/or custom fields you wish to sync.

2. **First Run (Detect Banned Characters)**

   ```powershell
   cd C:\Tools\SectigoSync\
   .\SectigoMetadataSync.exe SCtoKF
   ```

   * If Sectigo custom field names contain banned characters, you will see warnings in the log.
   * A file named `bannedCharacters.json` will be created listing each banned character with `"replacementCharacter": null`.

3. **Populate Banned Characters**

   * Open `bannedCharacters.json`.
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

5. **Ongoing Use**

   * Schedule the executable with the argument of your choice (`SCtoKF` or `KFtoSC`) once per week via Task Scheduler.
   * Each run will create any newly added fields and keep all values in sync.

---

### Troubleshooting

* **Missing or Invalid JSON**

  * If `stock-config.json` or `fields.json` is malformed or missing required sections, the tool logs an error and exits.
  * Ensure both files exist, are valid JSON, and contain the required properties (see “Settings” above).

* **Authentication Failures**

  * Double‐check `sectigoLogin`/`sectigoPassword` and `keyfactorLogin`/`keyfactorPassword`.
  * Ensure your API user has adequate permissions to create/update metadata fields.

* **Rate Limits or Timeouts**

  * Sectigo and Keyfactor may throttle large requests.
  * If you repeatedly see HTTP 429 or timeout errors, consider reducing `sectigoPageSize`/`keyfactorPageSize` in `stock-config.json`.

* **Field Creation Errors**

  * If Keyfactor rejects a field name (e.g., still contains a banned character), verify that `bannedCharacters.json` is up to date.
  * If Sectigo rejects a custom‐field update, ensure you are using the correct custom‐field “name” as visible in Sectigo’s administration UI.

---

**Copyright & License**
This tool and its source code are provided “as‐is.” Modify, redistribute, or use at your own risk. Check with your organization’s policies before deploying in a production environment.
