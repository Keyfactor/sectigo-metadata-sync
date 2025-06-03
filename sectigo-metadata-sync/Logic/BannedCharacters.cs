// Copyright 2021 Keyfactor
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
// You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
// and limitations under the License.

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using SectigoMetadataSync.Models;

namespace SectigoMetadataSync.Logic;

/// <summary>
///     Provides utilities for identifying and replacing banned characters in field names.
/// </summary>
public class BannedCharacters
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    ///     Parses a given string to identify characters that are not allowed (banned characters).
    /// </summary>
    /// <param name="input">The string to parse for banned characters.</param>
    /// <param name="invalidCharacterDetails">A list to store details about invalid characters found in the input.</param>
    /// <returns>A list of <see cref="CharDBItem" /> objects representing the banned characters and their replacements.</returns>
    public static List<CharDBItem> BannedCharacterParse(string input, List<string> invalidCharacterDetails)
    {
        var pattern = "[a-zA-Z0-9-_]";
        var bannedChars = new List<CharDBItem>();
        var uniqueCharacters = new HashSet<string>(); // Track unique banned characters

        foreach (var c in input)
            if (!Regex.IsMatch(c.ToString(), pattern) && uniqueCharacters.Add(c.ToString()))
            {
                var localitem = new CharDBItem
                {
                    character = c.ToString(),
                    replacementcharacter = "null"
                };
                bannedChars.Add(localitem);

                // Add details for aggregation
                invalidCharacterDetails.Add(
                    $"The field name '{input}' contains the invalid character: '{c}' (U+{(int)c:X4})");
            }

        return bannedChars;
    }

    /// <summary>
    ///     Checks a list of metadata fields for banned characters in their names.
    /// </summary>
    /// <param name="input">The list of <see cref="UnifiedFormatField" /> objects to check.</param>
    /// <param name="allBannedChars">A list to store all banned characters found across the fields.</param>
    /// <param name="invalidCharacterDetails">A list to store details about invalid characters found in the fields.</param>
    /// <param name="noAuto">
    ///     If true, checks the <see cref="UnifiedFormatField.KeyfactorMetadataFieldName" /> for banned characters.
    ///     Otherwise, checks the <see cref="UnifiedFormatField.SectigoFieldName" /> or
    ///     <see cref="UnifiedFormatField.KeyfactorMetadataFieldName" />
    ///     depending on the field type.
    /// </param>
    public static void CheckForChars(List<UnifiedFormatField> input, List<CharDBItem> allBannedChars,
        List<string> invalidCharacterDetails, bool noAuto = false)
    {
        foreach (var scField in input)
        {
            // Option 1 - Custom Field, Auto conversion - in this case we need to check the sectigo field name, as it is used for conversion
            var fieldName = scField.SectigoFieldName;
            // Option 2 - Manual Field 9 (auto and !auto) - in this case we only check the Keyfactor Field Name
            if (scField.ToolFieldType == UnifiedFieldType.Manual) fieldName = scField.KeyfactorMetadataFieldName;
            // Option 3, Custom Field, !Auto and Manual Loading - in this case we check the loaded Keyfactor Field Name, as no conversion is done
            if (noAuto) fieldName = scField.KeyfactorMetadataFieldName;
            var newChars = BannedCharacterParse(fieldName, invalidCharacterDetails);
            foreach (var newchar in newChars)
            {
                var exists = allBannedChars.Any(allcharchar => allcharchar.character == newchar.character);
                if (!exists) allBannedChars.Add(newchar);
            }
        }

        _logger.Info(
            $"Checked {input.Count} fields for banned characters.");
        if (invalidCharacterDetails.Count > 0)
            _logger.Warn(
                $"The following invalid characters were found in the field names: {string.Join(", ", invalidCharacterDetails)}");
    }

    /// <summary>
    ///     Replaces all banned characters in a string with their corresponding replacement characters.
    /// </summary>
    /// <param name="input">The string to sanitize by replacing banned characters.</param>
    /// <param name="allBannedChars">
    ///     A list of <see cref="CharDBItem" /> objects representing banned characters and their
    ///     replacements.
    /// </param>
    /// <returns>The sanitized string with banned characters replaced.</returns>
    public static string ReplaceAllBannedCharacters(string input, List<CharDBItem> allBannedChars)
    {
        var missingReplacements = new List<string>();
        var finalString = input;
        var conversionOccurred = false; // Track if any conversion has taken place

        foreach (var item in allBannedChars)
            if (item.replacementcharacter == "null")
            {
                missingReplacements.Add($"'{item.character}' (U+{(int)item.character[0]:X4})");
            }
            else
            {
                if (finalString.Contains(item.character))
                {
                    finalString = finalString.Replace(item.character, item.replacementcharacter);
                    conversionOccurred = true; // Mark that a conversion has occurred
                }
            }

        if (missingReplacements.Count > 0)
            _logger.Warn(
                $"The field name '{input}' has banned characters with no replacements: {string.Join(", ", missingReplacements)}");
        else if (conversionOccurred)
            _logger.Info(
                $"The Sectigo field name '{input}' will be converted to: '{finalString}' for use with Keyfactor.");

        return finalString;
    }
}