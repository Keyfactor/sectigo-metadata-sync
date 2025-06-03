using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SectigoMetadataSync.Models;

/// <summary>
///     Represents the input types available for Sectigo custom fields.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CustomFieldInputType
{
    TEXT_SINGLE_LINE,
    TEXT_MULTI_LINE,
    EMAIL,
    NUMBER,
    TEXT_OPTION,
    DATE
}

/// <summary>
///     Holds the input details for a Sectigo custom field, using a strongly-typed enum.
/// </summary>
public class CustomFieldInput
{
    /// <summary>
    ///     The input type for this custom field (as defined by Sectigo).
    /// </summary>
    [JsonPropertyName("type")]
    public CustomFieldInputType Type { get; set; }

    /// <summary>
    ///     Input field options (for 'TEXT_OPTION' type only).
    /// </summary>
    [JsonPropertyName("options")]
    public List<string>? Options { get; set; } = null;
}

/// <summary>
///     Represents a Sectigo custom field, with metadata and input type.
/// </summary>
public class SectigoCustomField
{
    [JsonPropertyName("id")] public int Id { get; set; }

    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     List of access methods for which this field is mandatory.
    /// </summary>
    [JsonPropertyName("mandatories")]
    public List<string> Mandatories { get; set; } = new();

    [JsonPropertyName("certType")] public string CertType { get; set; } = string.Empty;

    [JsonPropertyName("state")] public string State { get; set; } = string.Empty;

    [JsonPropertyName("input")] public CustomFieldInput Input { get; set; } = new();
}

/// <summary>
///     Represents an SSL certificate retrieved from the Sectigo API.
/// </summary>
public class SectigoCertificate
{
    [JsonPropertyName("sslId")] public int SslId { get; set; }

    [JsonPropertyName("commonName")] public string CommonName { get; set; } = string.Empty;

    [JsonPropertyName("subjectAlternativeNames")]
    public List<string>? SubjectAlternativeNames { get; set; } = null;

    [JsonPropertyName("serialNumber")] public string SerialNumber { get; set; } = string.Empty;
}

/// <summary>
///     Represents detailed information about an SSL certificate retrieved from the Sectigo API.
/// </summary>
public class SectigoCertificateDetails
{
    [JsonPropertyName("commonName")] public string CommonName { get; set; } = string.Empty;

    [JsonPropertyName("sslId")] public int SslId { get; set; }

    [JsonPropertyName("id")] public int Id { get; set; }

    [JsonPropertyName("orgId")] public int OrgId { get; set; }

    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;

    [JsonPropertyName("orderNumber")] public long OrderNumber { get; set; }

    [JsonPropertyName("backendCertId")] public string BackendCertId { get; set; } = string.Empty;

    [JsonPropertyName("vendor")] public string Vendor { get; set; } = string.Empty;

    [JsonPropertyName("certType")] public CertType CertType { get; set; } = new();

    [JsonPropertyName("subType")] public string SubType { get; set; } = string.Empty;

    [JsonPropertyName("validationType")] public string ValidationType { get; set; } = string.Empty;

    [JsonPropertyName("term")] public int Term { get; set; }

    [JsonPropertyName("owner")] public string Owner { get; set; } = string.Empty;

    [JsonPropertyName("ownerId")] public int OwnerId { get; set; }

    [JsonPropertyName("requester")] public string Requester { get; set; } = string.Empty;

    [JsonPropertyName("requestedVia")] public string RequestedVia { get; set; } = string.Empty;

    [JsonPropertyName("comments")] public string Comments { get; set; } = string.Empty;

    [JsonPropertyName("requested")] public string Requested { get; set; } = string.Empty;

    [JsonPropertyName("expires")] public string Expires { get; set; } = string.Empty;

    [JsonPropertyName("renewed")] public bool Renewed { get; set; }

    [JsonPropertyName("serialNumber")] public string SerialNumber { get; set; } = string.Empty;

    [JsonPropertyName("keyAlgorithm")] public string KeyAlgorithm { get; set; } = string.Empty;

    [JsonPropertyName("keySize")] public int KeySize { get; set; }

    [JsonPropertyName("keyType")] public string KeyType { get; set; } = string.Empty;

    [JsonPropertyName("subjectAlternativeNames")]
    public List<string>? SubjectAlternativeNames { get; set; } = null;

    [JsonPropertyName("customFields")] public List<CustomFieldDetails>? CustomFields { get; set; } = null;

    [JsonPropertyName("certificateDetails")]
    public CertificateDetails? CertificateDetails { get; set; } = null;

    [JsonPropertyName("autoInstallDetails")]
    public AutoInstallDetails? AutoInstallDetails { get; set; } = null;

    [JsonPropertyName("autoRenewDetails")] public AutoRenewDetails? AutoRenewDetails { get; set; } = null;

    [JsonPropertyName("suspendNotifications")]
    public bool SuspendNotifications { get; set; }
}

public class CertType
{
    [JsonPropertyName("id")] public int Id { get; set; }

    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;

    [JsonPropertyName("terms")] public List<int> Terms { get; set; } = new();

    [JsonPropertyName("keyTypes")] public KeyTypes KeyTypes { get; set; } = new();

    [JsonPropertyName("useSecondaryOrgName")]
    public bool UseSecondaryOrgName { get; set; }
}

public class KeyTypes
{
    [JsonPropertyName("RSA")] public List<string> RSA { get; set; } = new();
}

public class CertificateDetails
{
    [JsonPropertyName("issuer")] public string Issuer { get; set; } = string.Empty;

    [JsonPropertyName("sha1Hash")] public string Sha1Hash { get; set; } = string.Empty;
}

public class AutoInstallDetails
{
    [JsonPropertyName("state")] public string State { get; set; } = string.Empty;
}

public class AutoRenewDetails
{
    [JsonPropertyName("state")] public string State { get; set; } = string.Empty;
}

public class CustomFieldDetails
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;

    [JsonPropertyName("value")] public string Value { get; set; } = string.Empty;
}