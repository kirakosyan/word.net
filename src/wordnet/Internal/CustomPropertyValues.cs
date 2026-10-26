using System;
using System.Globalization;
using System.IO;
using WordNet.Model;

namespace WordNet.Internal;

internal static class CustomPropertyValues
{
    internal static string Display(DocumentProperty property) => property.Type switch
    {
        DocumentPropertyType.Text => (string)property.Value,
        DocumentPropertyType.Boolean => (bool)property.Value ? "true" : "false",
        DocumentPropertyType.Number => ((decimal)property.Value).ToString("0.############################", CultureInfo.InvariantCulture),
        DocumentPropertyType.Date => ((DateTimeOffset)property.Value).ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture),
        _ => throw new InvalidOperationException()
    };
    internal static void Validate(DocumentModel model, ConversionContext context)
    {
        long characters = (long)model.CharacterCount + model.Title.Length + model.Author.Length;
        if (model.CustomProperties.Count > 1024) throw new InvalidDataException("Limit.CustomProperties: At most 1024 properties are admitted.");
        foreach (DocumentProperty property in model.CustomProperties.Values)
        { context.CheckCancellation(); characters = checked(characters + property.Name.Length + Display(property).Length); }
        if (characters > context.Options.MaximumCharacters || characters * 2 + model.CustomProperties.Count * 128L > context.Options.MaximumExpandedBytes)
            throw new InvalidDataException("Limit.CustomProperties: Aggregate metadata/document limits exceeded.");
    }
}
