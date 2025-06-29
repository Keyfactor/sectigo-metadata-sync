// Copyright 2021 Keyfactor
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
// You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
// and limitations under the License.

using SectigoMetadataSync.Models;

namespace SectigoMetadataSync.Logic;

public class MappingLogic
{
    /// <summary>
    ///     Maps a Sectigo custom field to a unified format field.
    /// </summary>
    /// <param name="sectigoField">The Sectigo custom field to map.</param>
    /// <returns>A unified format field with mapped properties.</returns>
    public static UnifiedFormatField MapSectigoToUnified(SectigoCustomField sectigoField)
    {
        return new UnifiedFormatField
        {
            SectigoFieldName = sectigoField.Name,
            KeyfactorMetadataFieldName = sectigoField.Name, // Example mapping
            KeyfactorDescription = sectigoField.CertType, // Example mapping
            KeyfactorDataType = MapDataType(sectigoField.Input.Type),
            KeyfactorHint = null, // Add hint if needed
            KeyfactorValidation = null, // Add validation if needed
            KeyfactorEnrollment = 0, // Default to Optional
            KeyfactorMessage = null, // Add message if needed
            KeyfactorOptions = sectigoField.Input.Type == CustomFieldInputType.TEXT_OPTION
                ? sectigoField.Input.Options?.ToArray()
                : null, // Map options only for TEXT_OPTION
            KeyfactorDefaultValue = null, // Add default value if needed
            KeyfactorDisplayOrder = 0, // Add display order if needed
            KeyfactorCaseSensitive = false // Default to false
        };
    }

    private static int MapDataType(CustomFieldInputType inputType)
    {
        return inputType switch
        {
            CustomFieldInputType.TEXT_SINGLE_LINE => 1, // String
            CustomFieldInputType.TEXT_MULTI_LINE => 1, // String
            CustomFieldInputType.EMAIL => 1, // String
            CustomFieldInputType.NUMBER => 2, // Integer
            CustomFieldInputType.TEXT_OPTION => 3, // Multiple Choice
            CustomFieldInputType.DATE => 4, // Date
            _ => 1 // Default to String
        };
    }
}