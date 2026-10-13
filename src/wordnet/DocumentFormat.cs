namespace WordNet;

/// <summary>Identifies a format in the conversion scope.</summary>
/// <remarks>Use CanConvert to query supported directions; document features have separate limits.</remarks>
public enum DocumentFormat
{
    /// <summary>No format has been specified.</summary>
    Unknown = 0,

    /// <summary>Microsoft Word binary document (.doc).</summary>
    Doc = 1,

    /// <summary>Office Open XML WordprocessingML document (.docx).</summary>
    Docx = 2,

    /// <summary>Portable Document Format (.pdf), including scanned input with configured English OCR.</summary>
    Pdf = 3,

    /// <summary>Markdown text (.md).</summary>
    Markdown = 4,

    /// <summary>Plain text (.txt).</summary>
    Text = 5,

    /// <summary>Amazon Kindle Format 8 (.azw3).</summary>
    Azw3 = 6,

    /// <summary>EPUB electronic publication (.epub).</summary>
    Epub = 7,

    /// <summary>Mobipocket electronic publication (.mobi).</summary>
    Mobi = 8,

    /// <summary>XML Paper Specification (.xps); output only.</summary>
    Xps = 9,
}
