// Copyright 2021 Keyfactor
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
// You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
// and limitations under the License.

using System;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using SectigoMetadataSync.Models;

namespace SectigoMetadataSync.Logic;

public class Helpers
{
    /// <summary>
    ///     Verifies the provided mode.
    /// </summary>
    public static bool CheckMode(string mode)
    {
        if (mode == "kftosc" || mode == "sctokf") return true;
        return false;
    }

    public static MetadataDataType ToKeyfactorDataType(CustomFieldInputType inputType)
    {
        return inputType switch
        {
            CustomFieldInputType.TEXT_SINGLE_LINE => MetadataDataType.String,
            CustomFieldInputType.TEXT_MULTI_LINE => MetadataDataType.BigText,
            CustomFieldInputType.EMAIL => MetadataDataType.Email,
            CustomFieldInputType.NUMBER => MetadataDataType.Integer,
            CustomFieldInputType.TEXT_OPTION => MetadataDataType.MultipleChoice,
            CustomFieldInputType.DATE => MetadataDataType.Date,
            _ => MetadataDataType.String
        };
    }

    /// <summary>
    ///     Retrieves the value of a property from a SectigoCertificateDetails instance based on a string path.
    ///     The path can include nested properties separated by dots (e.g., "certificateDetails.sha1Hash").
    /// </summary>
    /// <param name="certDetails">The instance of SectigoCertificateDetails to retrieve the value from.</param>
    /// <param name="path">The string path pointing to the desired property (e.g., "renewed" or "certificateDetails.sha1Hash").</param>
    /// <returns>The value of the property, or null if the path is invalid or the property does not exist.</returns>
    public static object? GetPropertyValue(SectigoCertificateDetails certDetails, string path)
    {
        if (certDetails == null) throw new ArgumentNullException(nameof(certDetails));
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path cannot be null or empty.", nameof(path));

        object? currentObject = certDetails;
        foreach (var propertyName in path.Split('.'))
        {
            if (currentObject == null) return null;

            // Get all public instance properties
            var properties = currentObject.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

            // Find the property by name (case-insensitive) or by JsonPropertyName attribute
            var propertyInfo = properties.FirstOrDefault(p =>
                string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase) ||
                p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name == propertyName);

            if (propertyInfo == null) return null; // Property not found

            // Get the value of the property
            currentObject = propertyInfo.GetValue(currentObject);
        }

        return currentObject;
    }
}