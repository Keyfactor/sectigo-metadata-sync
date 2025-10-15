// Copyright 2024 Keyfactor
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
// and limitations under the License.

using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using SectigoMetadataSync.Models;

namespace SectigoMetadataSync.Logic;

/// <summary>
///     Coercion helpers for pushing Keyfactor metadata values into Sectigo custom fields.
///     Mirrors the approach used by the DigiCert implementation, but targets Sectigo's input types.
/// </summary>
public static class ValueCoercionSC
{
    private const int MaxCustomFieldValue = 256;

    // Keep the same email validator pattern used elsewhere for consistency.
    private static readonly Regex EmailRx = new(@"^[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool _enableTruncation = false;
    public static bool EnableTruncation
    {
        get => _enableTruncation;
        set => _enableTruncation = value;
    }
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    ///     Coerces an input value (usually from Keyfactor) into a Sectigo-compatible string value
    ///     based on the Sectigo field's input type.
    /// </summary>
    /// <param name="value">Raw value, typically a Keyfactor metadata string.</param>
    /// <param name="inputType">Sectigo custom field input type (TEXT_SINGLE_LINE, NUMBER, DATE, etc.).</param>
    /// <param name="kfOptions">
    ///     Optional Keyfactor-side options for Multiple Choice style fields; if provided for TEXT_OPTION, we try to
    ///     normalize against these options so we push a canonical value.
    /// </param>
    /// <param name="kfDateFormat">
    ///     The date format Keyfactor uses for Date metadata (e.g., "yyyy-MM-dd" or "M/d/yyyy h:mm:ss tt").
    ///     When provided for DATE fields, this is used to parse the incoming value and emit "yyyy-MM-dd".
    /// </param>
    /// <returns>String value suitable for Sectigo's API, or null when the value is invalid/empty for the target type.</returns>
    public static string? CoerceForSectigo(string? value,
        CustomFieldInputType inputType,
        string[]? kfOptions = null,
        string? kfDateFormat = "yyyy-MM-dd")
    {
        if (value is null) return null;

        var s = value.Trim();
        if (s.Length == 0) return null;

        switch (inputType)
        {
            case CustomFieldInputType.TEXT_SINGLE_LINE:
            case CustomFieldInputType.TEXT_MULTI_LINE:
                return Truncate(s, MaxCustomFieldValue); // cap to 256

            case CustomFieldInputType.EMAIL:
                return EmailRx.IsMatch(s) ? Truncate(s, MaxCustomFieldValue) : null;

            case CustomFieldInputType.NUMBER:
                return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                    ? Truncate(n.ToString(CultureInfo.InvariantCulture), MaxCustomFieldValue)
                    : null;

            case CustomFieldInputType.TEXT_OPTION:
            {
                if (kfOptions is { Length: > 0 })
                {
                    var norm = NormalizeChoice(s);
                    var match = kfOptions.FirstOrDefault(o => NormalizeChoice(o) == norm);
                    return Truncate(match ?? s, MaxCustomFieldValue);
                }

                return Truncate(s, MaxCustomFieldValue);
            }

            case CustomFieldInputType.DATE:
            {
                if (!string.IsNullOrWhiteSpace(kfDateFormat) &&
                    DateTime.TryParseExact(s, kfDateFormat, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var dtExact))
                    return Truncate(dtExact.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), MaxCustomFieldValue);

                if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out var dto))
                    return Truncate(dto.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), MaxCustomFieldValue);

                if (DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var dtYmd))
                    return Truncate(dtYmd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), MaxCustomFieldValue);

                return null;
            }

            default:
                // Treat unknown types as text
                return Truncate(s, MaxCustomFieldValue);
        }
    }

    /// <summary>
    ///     Looser variant that attempts to coerce values based solely on Keyfactor-type hints.
    ///     Useful if you do not have a Sectigo input type handy.
    /// </summary>
    public static string? CoerceFromKeyfactorType(string? value, int keyfactorDataTypeCode, string[]? kfOptions = null,
        string? kfDateFormat = "yyyy-MM-dd")
    {
        if (value is null) return null;
        var s = value.Trim();
        if (s.Length == 0) return null;

        switch (keyfactorDataTypeCode)
        {
            case 2: // Integer
                return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                    ? Truncate(n.ToString(CultureInfo.InvariantCulture), MaxCustomFieldValue)
                    : null;

            case 4: // Date
                if (!string.IsNullOrWhiteSpace(kfDateFormat) &&
                    DateTime.TryParseExact(s, kfDateFormat, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var dtExact))
                    return Truncate(dtExact.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), MaxCustomFieldValue);

                if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out var dto))
                    return Truncate(dto.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), MaxCustomFieldValue);

                if (DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var dtYmd))
                    return Truncate(dtYmd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), MaxCustomFieldValue);

                return null;

            case 3: // MultipleChoice
                if (kfOptions is { Length: > 0 })
                {
                    var norm = NormalizeChoice(s);
                    var match = kfOptions.FirstOrDefault(o => NormalizeChoice(o) == norm);
                    return Truncate(match ?? s, MaxCustomFieldValue);
                }

                return Truncate(s, MaxCustomFieldValue);

            // String / BigText / Email -> best-effort email sanity then cap
            default:
                if (s.Contains('@'))
                    return EmailRx.IsMatch(s) ? Truncate(s, MaxCustomFieldValue) : null;

                return Truncate(s, MaxCustomFieldValue);
        }
    }

    private static string? Truncate(string? x, int max, string? context = null)
    {
        if (string.IsNullOrEmpty(x)) return x;
        if (x.Length <= max) return x;

        if (!EnableTruncation)
        {
            _logger.Warn(
                $"Input value exceeds Sectigo limits on character length for Custom Field contents ({x.Length} > {max}) and truncation is disabled in config. Original value will be returned and will result in the value not getting synced.");
            return x;
        }
        // Keep logs metadata-only; do not echo the actual value.
        // Example contexts: "sectigo.custom_field.name", "sectigo.custom_field.value", "sectigo.comments"
        try
        {
            _logger.Warn(
                "WARNING: Keyfactor metadata field contents exceed Sectigo limits on character length and truncation is enabled. Truncating {Context} from {OriginalLength} to {MaxLength} characters.",
                context ?? "value", x.Length, max);
        }
        catch
        {
            // Never throw from logging
        }

        return x.Substring(0, max);
    }

    private static string NormalizeChoice(string x)
    {
        return Regex.Replace(x.Trim(), @"\s+", " ").ToLowerInvariant();
    }
}