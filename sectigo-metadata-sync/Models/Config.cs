using System.Collections.Generic;

namespace SectigoMetadataSync.Models;

public class Config
{
    public string sectigoLogin { get; set; } = "";
    public string sectigoPassword { get; set; } = "";
    public string sectigoCustomerUri { get; set; } = "";
    public string sectigoAPIUrl { get; set; } = "";
    public string keyfactorAPIUrl { get; set; } = "";
    public string keyfactorLogin { get; set; } = "";
    public string keyfactorPassword { get; set; } = "";
    public string keyfactorDateFormat { get; set; } = "M/d/yyyy h:mm:ss tt";
    public bool importAllCustomFields { get; set; } = false;
    public bool syncRevokedAndExpiredCerts { get; set; } = false;
    public string issuerDNLookupTerm { get; set; } = "Sectigo";
    public List<int> sslTypeIds { get; set; } = new();
    public bool enableDisabledFieldSync { get; set; } = false;
    public int sectigoPageSize { get; set; } = 25;
    public int keyfactorPageSize { get; set; } = 100;
}

public class ManualField
{
    public string sectigoFieldName { get; set; } = "";
    public string keyfactorMetadataFieldName { get; set; } = "";
    public string keyfactorDescription { get; set; } = "";
    public string keyfactorDataType { get; set; } = "";
    public string keyfactorHint { get; set; } = "";
    public string keyfactorAllowAPI { get; set; } = "";
}

public class CustomField
{
    public string sectigoFieldName { get; set; } = "";
    public string keyfactorMetadataFieldName { get; set; } = "";
    public string keyfactorDescription { get; set; } = "";
    public string keyfactorDataType { get; set; } = "";
    public string keyfactorHint { get; set; } = "";
    public string keyfactorAllowAPI { get; set; } = "";
}

/// <summary>
///     Represents the configuration mode for the application.
/// </summary>
public enum ConfigMode
{
    KFtoSC, // Keyfactor to Sectigo
    SCtoKF // Sectigo to Keyfactor
}