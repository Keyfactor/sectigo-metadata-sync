// Copyright 2021 Keyfactor
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
// You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
// and limitations under the License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using SectigoMetadataSync.Models;

namespace SectigoMetadataSync.Logic;

public class Helpers
{
    public static IConfigurationSection GetRootConfigSection(IConfiguration cfg)
    {
        // Support either "Config" or "config" in JSON
        var sec = cfg.GetSection("Config");
        if (!sec.Exists()) sec = cfg.GetSection("config");
        return sec;
    }

    public static bool IsOAuthBlockUsable(KeyfactorOAuthOptions? opt)
    {
        if (opt is null) return false;
        // Minimal requirements to consider OAuth “present”
        if (string.IsNullOrWhiteSpace(opt.TokenUrl)) return false;
        if (string.IsNullOrWhiteSpace(opt.ClientId)) return false;
        if (string.IsNullOrWhiteSpace(opt.ClientSecret)) return false;
        return true;
    }

    public static KeyfactorMetadataDataType ToKeyfactorDataType(CustomFieldInputType inputType)
    {
        return inputType switch
        {
            CustomFieldInputType.TEXT_SINGLE_LINE => KeyfactorMetadataDataType.String,
            CustomFieldInputType.TEXT_MULTI_LINE => KeyfactorMetadataDataType.BigText,
            CustomFieldInputType.EMAIL => KeyfactorMetadataDataType.Email,
            CustomFieldInputType.NUMBER => KeyfactorMetadataDataType.Integer,
            CustomFieldInputType.TEXT_OPTION => KeyfactorMetadataDataType.MultipleChoice,
            CustomFieldInputType.DATE => KeyfactorMetadataDataType.Date,
            _ => KeyfactorMetadataDataType.String
        };
    }

    // Mirrors: public static JObject Flatten(JObject jObject, string parentName = "")
    public static JsonObject Flatten(JsonObject obj, string parentName = "")
    {
        if (obj is null) throw new ArgumentNullException(nameof(obj));

        var result = new JsonObject();

        void Recurse(JsonNode? node, string prefix)
        {
            switch (node)
            {
                case JsonObject o:
                    foreach (var kvp in o)
                    {
                        var name = string.IsNullOrEmpty(prefix) ? kvp.Key : $"{prefix}.{kvp.Key}";
                        Recurse(kvp.Value, name);
                    }

                    break;

                case JsonArray arr:
                    for (var i = 0; i < arr.Count; i++)
                    {
                        var name = string.IsNullOrEmpty(prefix) ? $"[{i}]" : $"{prefix}[{i}]";
                        Recurse(arr[i], name);
                    }

                    break;

                default:
                    // Leaf (string/number/bool/null). DeepClone to detach from source graph.
                    result[prefix] = node?.DeepClone();
                    break;
            }
        }

        Recurse(obj, parentName ?? string.Empty);
        return result;
    }

    /// <summary>
    ///     Resolves a dot-path on an object graph using reflection and [JsonPropertyName] matches.
    ///     If stringifyLeaf=true, collections/JsonElement/etc. are converted to a scalar string.
    /// </summary>
    public static object? GetPropertyValue(object root, string path, bool stringifyLeaf = true)
    {
        if (root is null) throw new ArgumentNullException(nameof(root));
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path cannot be null or empty.", nameof(path));

        var current = root;

        foreach (var propertyName in path.Split('.',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current is null) return null;

            // Support JsonElement containers too
            if (current is JsonElement je && je.ValueKind == JsonValueKind.Object)
            {
                if (!je.TryGetProperty(propertyName, out je)) return null;
                current = je;
                continue;
            }

            var props = current.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var pi = props.FirstOrDefault(p =>
                string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase) ||
                p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name == propertyName);

            if (pi is null) return null;
            current = pi.GetValue(current);
        }

        return stringifyLeaf ? CoerceToScalarString(current) : current;
    }

    private static string? CoerceToScalarString(object? value)
    {
        if (value is null) return null;

        // Already string
        if (value is string s) return s;

        // JsonElement -> scalar/CSV/JSON
        if (value is JsonElement je) return JsonElementToString(je);

        // Date/Time
        if (value is DateTime dt) return dt.ToString("O", CultureInfo.InvariantCulture);
        if (value is DateTimeOffset dto) return dto.ToString("O", CultureInfo.InvariantCulture);

        // Numbers/booleans/etc.
        if (value is IFormattable f) return f.ToString(null, CultureInfo.InvariantCulture);

        // IEnumerable -> CSV
        if (value is IEnumerable enumerable && value is not IEnumerable<char>)
        {
            var parts = new List<string>();
            foreach (var item in enumerable)
            {
                var text = CoerceToScalarString(item);
                if (!string.IsNullOrWhiteSpace(text))
                    parts.Add(text);
            }

            return parts.Count == 0 ? null : string.Join(", ", parts);
        }

        // Complex object -> stable JSON snapshot
        return JsonSerializer.Serialize(value);
    }

    private static string? JsonElementToString(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => null,
            JsonValueKind.Array => string.Join(", ",
                el.EnumerateArray()
                    .Select(JsonElementToString)
                    .Where(x => !string.IsNullOrWhiteSpace(x))),
            JsonValueKind.Object => el.GetRawText(),
            _ => el.GetRawText()
        };
    }
}