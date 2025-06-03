using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SectigoMetadataSync.Models;

public class KeyfactorMetadataField
{
    [JsonPropertyName("Id")] public int Id { get; set; } = 0; // Default to 0
    [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty; // Default to empty string
    [JsonPropertyName("Description")] public string Description { get; set; } = string.Empty; // Default to empty string
    [JsonPropertyName("DataType")] public int DataType { get; set; } = 0; // Default to 0
    [JsonPropertyName("Hint")] public string? Hint { get; set; } = null; // Nullable
    [JsonPropertyName("Validation")] public string? Validation { get; set; } = null; // Nullable
    [JsonPropertyName("Enrollment")] public int Enrollment { get; set; } = 0; // Default to 0 (Optional)
    [JsonPropertyName("Message")] public string? Message { get; set; } = null; // Nullable
    [JsonPropertyName("Options")] public string? Options { get; set; } = null; // Nullable
    [JsonPropertyName("DefaultValue")] public string? DefaultValue { get; set; } = null; // Nullable
    [JsonPropertyName("DisplayOrder")] public int DisplayOrder { get; set; } = 0; // Default to 0
    [JsonPropertyName("CaseSensitive")] public bool CaseSensitive { get; set; } = false; // Default to false
}

/// <summary>
///     Represents the data types for Keyfactor metadata fields.
/// </summary>
public enum MetadataDataType
{
    String = 1,
    Integer = 2,
    Date = 3,
    Boolean = 4,
    MultipleChoice = 5,
    BigText = 6,
    Email = 7
}

public class KeyfactorCertificate
{
    public int Id { get; set; } = 0; // Default to 0
    public string Thumbprint { get; set; } = string.Empty; // Default to empty string
    public string SerialNumber { get; set; } = string.Empty; // Default to empty string
    public string IssuedDN { get; set; } = string.Empty; // Default to empty string
    public string IssuedCN { get; set; } = string.Empty; // Default to empty string
    public DateTime ImportDate { get; set; } = DateTime.MinValue; // Default to MinValue
    public DateTime NotBefore { get; set; } = DateTime.MinValue; // Default to MinValue
    public DateTime NotAfter { get; set; } = DateTime.MinValue; // Default to MinValue
    public string IssuerDN { get; set; } = string.Empty; // Default to empty string
    public object PrincipalId { get; set; } = new(); // Default to new object
    public int? OwnerRoleId { get; set; } = null; // Nullable
    public string OwnerRoleName { get; set; } = string.Empty; // Default to empty string
    public int? TemplateId { get; set; } = null; // Nullable
    public int CertState { get; set; } = 0; // Default to 0
    public int KeySizeInBits { get; set; } = 0; // Default to 0
    public int KeyType { get; set; } = 0; // Default to 0
    public string KeyAlgorithm { get; set; } = string.Empty; // Default to empty string
    public object AltKeyAlgorithm { get; set; } = new(); // Default to new object
    public int AltKeySizeInBits { get; set; } = 0; // Default to 0
    public object AltKeyType { get; set; } = new(); // Default to new object
    public int? RequesterId { get; set; } = null; // Nullable
    public string IssuedOU { get; set; } = string.Empty; // Default to empty string
    public string IssuedEmail { get; set; } = string.Empty; // Default to empty string
    public int KeyUsage { get; set; } = 0; // Default to 0
    public string SigningAlgorithm { get; set; } = string.Empty; // Default to empty string
    public object AltSigningAlgorithm { get; set; } = new(); // Default to new object
    public string CertStateString { get; set; } = string.Empty; // Default to empty string
    public string KeyTypeString { get; set; } = string.Empty; // Default to empty string
    public object AltKeyTypeString { get; set; } = new(); // Default to new object
    public DateTime? RevocationEffDate { get; set; } = null; // Nullable
    public int? RevocationReason { get; set; } = null; // Nullable
    public object RevocationComment { get; set; } = new(); // Default to new object
    public int? CertificateAuthorityId { get; set; } = null; // Nullable
    public string CertificateAuthorityName { get; set; } = string.Empty; // Default to empty string
    public string TemplateName { get; set; } = string.Empty; // Default to empty string
    public bool ArchivedKey { get; set; } = false; // Default to false
    public bool HasPrivateKey { get; set; } = false; // Default to false
    public bool HasAltPrivateKey { get; set; } = false; // Default to false
    public string PrincipalName { get; set; } = string.Empty; // Default to empty string
    public object CertRequestId { get; set; } = new(); // Default to new object
    public string RequesterName { get; set; } = string.Empty; // Default to empty string
    public string ContentBytes { get; set; } = string.Empty; // Default to empty string

    public Extendedkeyusage[] ExtendedKeyUsages { get; set; } =
        Array.Empty<Extendedkeyusage>(); // Default to empty array

    public Subjectaltnameelement[] SubjectAltNameElements { get; set; } =
        Array.Empty<Subjectaltnameelement>(); // Default to empty array

    public Crldistributionpoint[] CRLDistributionPoints { get; set; } =
        Array.Empty<Crldistributionpoint>(); // Default to empty array

    public object[] LocationsCount { get; set; } = Array.Empty<object>(); // Default to empty array
    public object[] SSLLocations { get; set; } = Array.Empty<object>(); // Default to empty array
    public object[] Locations { get; set; } = Array.Empty<object>(); // Default to empty array

    [JsonPropertyName("Metadata")] public Dictionary<string, string>? Metadata { get; set; } = null; // Nullable

    public int? CARowIndex { get; set; } = null; // Nullable
    public string CARecordId { get; set; } = string.Empty; // Default to empty string
    public Detailedkeyusage DetailedKeyUsage { get; set; } = new(); // Default to new instance
    public bool KeyRecoverable { get; set; } = false; // Default to false
    public object Curve { get; set; } = new(); // Default to new object
    public object EnrollmentPatternId { get; set; } = new(); // Default to new object
}

public class Detailedkeyusage
{
    public bool CrlSign { get; set; } = false; // Default to false
    public bool DataEncipherment { get; set; } = false; // Default to false
    public bool DecipherOnly { get; set; } = false; // Default to false
    public bool DigitalSignature { get; set; } = false; // Default to false
    public bool EncipherOnly { get; set; } = false; // Default to false
    public bool KeyAgreement { get; set; } = false; // Default to false
    public bool KeyCertSign { get; set; } = false; // Default to false
    public bool KeyEncipherment { get; set; } = false; // Default to false
    public bool NonRepudiation { get; set; } = false; // Default to false
    public string HexCode { get; set; } = string.Empty; // Default to empty string
}

public class Extendedkeyusage
{
    public int Id { get; set; } = 0; // Default to 0
    public string Oid { get; set; } = string.Empty; // Default to empty string
    public string DisplayName { get; set; } = string.Empty; // Default to empty string
}

public class Subjectaltnameelement
{
    public int Id { get; set; } = 0; // Default to 0
    public string Value { get; set; } = string.Empty; // Default to empty string
    public int Type { get; set; } = 0; // Default to 0
    public string ValueHash { get; set; } = string.Empty; // Default to empty string
}

public class Crldistributionpoint
{
    public int Id { get; set; } = 0; // Default to 0
    public string Url { get; set; } = string.Empty; // Default to empty string
    public string UrlHash { get; set; } = string.Empty; // Default to empty string
}