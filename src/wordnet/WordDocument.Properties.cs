using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using WordNet.Internal;

namespace WordNet;

public sealed partial class WordDocument
{
    /// <summary>Gets a detached, immutable snapshot of custom properties in name order.</summary>
    public IReadOnlyList<DocumentProperty> CustomProperties
    {
        get { lock (gate) return model.CustomProperties.Values.OrderBy(p => p.Name, StringComparer.Ordinal).ToList().AsReadOnly(); }
    }
    /// <summary>Atomically adds or replaces one typed property. Names match ordinally ignoring case.</summary>
    public void SetCustomProperty(DocumentProperty property, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(property, nameof(property));
        _ = Edit((m, c) => { m.CustomProperties[property.Name] = property; CustomPropertyValues.Validate(m, c); return 1; }, cancellationToken, reviewAware: true, objectAware: true);
    }
    /// <summary>Atomically removes a custom property by case-insensitive name; returns whether it existed.</summary>
    public bool RemoveCustomProperty(string name, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(name, nameof(name));
        return Edit((m, c) => m.CustomProperties.Remove(name) ? 1 : 0, cancellationToken, reviewAware: true, objectAware: true).Changes != 0;
    }
}
