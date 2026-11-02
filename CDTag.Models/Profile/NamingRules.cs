namespace CDTag.Model.Profile
{
    /// <summary>A find/replace pair applied to names (CD-Tag 2.x ORIG_LIST / REPLACE_LIST).</summary>
    public class ReplacePair
    {
        public string Find { get; set; }
        public string Replace { get; set; }
    }

    /// <summary>Character and casing rules used when building file and folder names.</summary>
    public class NamingRules
    {
        /// <summary>Applied in order, to field values and then to the whole name.</summary>
        public ReplacePair[] Replacements { get; set; }

        /// <summary>Strings removed from names.</summary>
        public string[] RemoveStrings { get; set; }

        /// <summary>Words kept lower case by &lt;SC&gt; (unless they start a field), e.g. "and", "of".</summary>
        public string[] LowercaseWords { get; set; }

        /// <summary>Characters that separate words for &lt;SC&gt; (e.g. "_", " ").</summary>
        public string[] WordSeparators { get; set; }

        /// <summary>Like word separators, but the next word is always capitalised (e.g. "-").</summary>
        public string[] FieldSeparators { get; set; }
    }

    /// <summary>One value inside an &lt;ExtInfo&gt; tag, e.g. Field "Release Type", Format "&lt;Value&gt;".</summary>
    public class ExtInfoItem
    {
        public string Field { get; set; }
        public string Format { get; set; }
    }

    /// <summary>
    /// Definition of &lt;ExtInfo&gt; (index 0) or &lt;ExtInfoN&gt; (index N-1). When at least one item has a value,
    /// the values are joined with <see cref="FieldSeparator"/> and wrapped in <see cref="Begin"/>/<see cref="End"/>.
    /// </summary>
    public class ExtInfoDefinition
    {
        public string Begin { get; set; }
        public string FieldSeparator { get; set; }
        public string End { get; set; }
        public ExtInfoItem[] Items { get; set; }
    }

    /// <summary>A dropdown choice: <see cref="Nfo"/> is shown and written to the NFO, <see cref="Dir"/> goes into names.</summary>
    public class ReleaseListItem
    {
        public string Nfo { get; set; }
        public string Dir { get; set; }

        public override string ToString()
        {
            return Nfo ?? "";
        }
    }

    /// <summary>Choices and defaults for the release information page of Tag Album.</summary>
    public class ReleaseInfoOptions
    {
        public ReleaseListItem[] ReleaseTypes { get; set; }
        public ReleaseListItem[] Sources { get; set; }
        public ReleaseListItem[] Languages { get; set; }
        public ReleaseListItem[] LimitedEdition { get; set; }
        public ReleaseListItem[] WhiteLabel { get; set; }
        public ReleaseListItem[] CleanExplicit { get; set; }

        public string GroupInitials { get; set; }
        public string Ripper { get; set; }
        public string Supplier { get; set; }
        public string Encoder { get; set; }
        public string RippingTool { get; set; }
        public string Comment { get; set; }

        public bool LockRipper { get; set; }
        public bool LockSupplier { get; set; }
        public bool LockEncoder { get; set; }
        public bool LockComment { get; set; }

        /// <summary>Date format used in the NFO, e.g. "MM-DD-YYYY" (2.x syntax).</summary>
        public string NfoDateFormat { get; set; }
    }
}
