namespace SectigoMetadataSync.Models;

/// <summary>
///     Represents a unified format for metadata fields.
/// </summary>
public class UnifiedFormatField
{
    public string SectigoFieldName { get; set; } = string.Empty;
    public string KeyfactorMetadataFieldName { get; set; } = string.Empty;
    public string KeyfactorDescription { get; set; } = string.Empty;
    public int KeyfactorDataType { get; set; }
    public string? KeyfactorHint { get; set; }
    public string? KeyfactorValidation { get; set; }
    public int KeyfactorEnrollment { get; set; } = 0; // Default to Optional
    public string? KeyfactorMessage { get; set; }
    public string[]? KeyfactorOptions { get; set; } // Added for options mapping
    public string? KeyfactorDefaultValue { get; set; }
    public int KeyfactorDisplayOrder { get; set; }
    public bool KeyfactorCaseSensitive { get; set; } = false; // Default to false
    public int KeyfactorMetadataFieldId { get; set; } = 0; // Default to 0 (not set)
    public UnifiedFieldType ToolFieldType { get; set; } = UnifiedFieldType.Custom; // Default to Custom
}

/// <summary>
///     Used for storage of replacement characters during field conversion.
/// </summary>
public class CharDBItem
{
    public string character { get; set; } = string.Empty;
    public string replacementcharacter { get; set; } = string.Empty;
}

public enum UnifiedFieldType
{
    Manual = 1,
    Custom = 2
}