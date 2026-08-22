namespace PigComic.Core.Package;

/// <summary>
/// Thrown when a .pcml package cannot be read at all (not a zip, missing
/// content.xml, unparseable XML, wrong root element). Validation-level problems
/// (§5.7) do NOT throw — they open the document read-only.
/// </summary>
public class PcmlLoadException : Exception
{
    public PcmlLoadException(string message) : base(message)
    {
    }

    public PcmlLoadException(string message, Exception inner) : base(message, inner)
    {
    }
}