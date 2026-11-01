using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace WordNet;

public sealed partial class TemplateDataSchema
{
    /// <summary>Gets a deterministic SHA-256 of the bounded declaration set.</summary>
    /// <remarks>The hash can fingerprint or reveal guessed schema names. It does not identify a template, options, fonts, providers or engine and cannot authorize plan reuse.</remarks>
    public string IdentitySha256 { get; }

    internal static string ComputeIdentity(IEnumerable<TemplateSchemaPath> declarations)
    {
        // Path segments are restricted to ASCII identifiers, so no JSON escaping is needed.
        // Properties and declarations use ordinal ordering and UTF-8 canonical bytes.
        var json = new StringBuilder("{\"paths\":["); bool first = true;
        foreach (TemplateSchemaPath path in declarations.OrderBy(item => item.RepeatCollectionPath != null)
                     .ThenBy(item => item.RepeatCollectionPath, StringComparer.Ordinal).ThenBy(item => item.Path, StringComparer.Ordinal))
        {
            if (!first) json.Append(','); first = false;
            json.Append("{\"path\":\"").Append(path.Path).Append("\",\"required\":").Append(path.Required ? "true" : "false")
                .Append(",\"scope\":");
            if (path.RepeatCollectionPath == null) json.Append("null"); else json.Append('"').Append(path.RepeatCollectionPath).Append('"');
            json.Append(",\"type\":").Append(((int)path.Type).ToString(CultureInfo.InvariantCulture)).Append('}');
        }
        json.Append("],\"schemaVersion\":1}");
        byte[] bytes = Encoding.UTF8.GetBytes(json.ToString());
#if NET8_0_OR_GREATER
        byte[] hash = SHA256.HashData(bytes);
#else
        using var algorithm = SHA256.Create(); byte[] hash = algorithm.ComputeHash(bytes);
#endif
#if NET10_0_OR_GREATER
        return Convert.ToHexStringLower(hash);
#elif NET8_0_OR_GREATER
        return Convert.ToHexString(hash).ToLowerInvariant();
#else
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
#endif
    }
}
