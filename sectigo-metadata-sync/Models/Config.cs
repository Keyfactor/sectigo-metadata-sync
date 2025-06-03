// Copyright 2021 Keyfactor
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
// You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
// and limitations under the License.

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