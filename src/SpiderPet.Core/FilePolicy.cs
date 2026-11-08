namespace SpiderPet.Core;

public enum FileVerdict
{
    /// <summary>Good enough for the spider's collection.</summary>
    Worthy,
    /// <summary>Rubbish. Gets dumped as far from the spider's corner as possible.</summary>
    Junk,
}

/// <param name="Quip">What the spider says about it.</param>
public sealed record FileJudgement(FileVerdict Verdict, string Quip);

/// <summary>
/// The spider's taste in files, decided by name and extension only (nothing is ever opened).
/// Deterministic: the same file name always gets the same verdict and the same remark.
/// </summary>
public static class FilePolicy
{
    // Whole words that must appear in the name (so "edge" does not match "knowledge").
    private static readonly (string Word, string Quip)[] JunkWords =
    {
        ("copilot", "Copilot? Absolutely not."),
        ("edge", "Edge. Hard pass."),
        ("teams", "Teams. I have standards."),
        ("onedrive", "OneDrive. Over there, please."),
        ("bing", "Bing. Eww."),
        ("msn", "MSN? In this economy?"),
        ("cortana", "Cortana is dead. Go away."),
        ("xbox", "Xbox slop. Out."),
        ("skype", "Skype. Haven't heard that name in years. Bye."),
        ("linkedin", "Corporate. Away with it."),
        ("recall", "Recall? I'll never forget that I hate it."),
        ("outlook", "Outlook is not looking good."),
        ("clippy", "...it looks like you're being thrown out."),
        ("tips", "Tips: stop putting this on my desktop."),
    };

    // Phrases that may contain several words.
    private static readonly (string Phrase, string Quip)[] JunkPhrases =
    {
        ("microsoft 365", "Microsoft 365. 365 reasons no."),
        ("office 365", "Office 365. Subscription slop."),
        ("windows ai", "AI slop. Out."),
        ("game bar", "Game bar? More like bar the door."),
        ("get started", "'Get started'. Get gone."),
    };

    private static readonly string[] JunkExtensions =
        { ".tmp", ".temp", ".bak", ".old", ".crdownload", ".part", ".dmp", ".log", ".ds_store" };

    private static readonly string[] JunkExtensionQuips =
    {
        "Trash. Taking it out.",
        "That's not a file, that's a leftover.",
        "Ew. Dust and crumbs.",
    };

    private static readonly string[] WorthyQuips =
    {
        "Ooh, shiny.",
        "Mine now.",
        "For my collection.",
        "Acceptable.",
        "I'll keep this one.",
        "Nice. Hoarding it.",
        "Finally, something good.",
    };

    /// <summary>Extra words (case-insensitive, whole words) that make a file junk.</summary>
    public static ISet<string> ExtraJunkWords { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static FileJudgement Judge(string fileName)
    {
        string name = fileName ?? "";
        string lower = name.ToLowerInvariant();
        string ext = System.IO.Path.GetExtension(lower);
        string stem = System.IO.Path.GetFileNameWithoutExtension(lower);

        var words = new HashSet<string>(StringComparer.Ordinal);
        foreach (string w in stem.Split(new[] { ' ', '-', '_', '.', '(', ')', '[', ']', '+' }, StringSplitOptions.RemoveEmptyEntries))
            words.Add(w);

        foreach ((string phrase, string quip) in JunkPhrases)
            if (stem.Contains(phrase, StringComparison.Ordinal))
                return new FileJudgement(FileVerdict.Junk, quip);

        foreach ((string word, string quip) in JunkWords)
            if (words.Contains(word))
                return new FileJudgement(FileVerdict.Junk, quip);

        foreach (string extra in ExtraJunkWords)
            if (words.Contains(extra.ToLowerInvariant()))
                return new FileJudgement(FileVerdict.Junk, $"{extra}? No thanks.");

        if (Array.IndexOf(JunkExtensions, ext) >= 0)
            return new FileJudgement(FileVerdict.Junk, JunkExtensionQuips[Pick(lower, JunkExtensionQuips.Length)]);

        if (words.Contains("spider") || words.Contains("web"))
            return new FileJudgement(FileVerdict.Worthy, "A file after my own heart.");

        return new FileJudgement(FileVerdict.Worthy, WorthyQuips[Pick(lower, WorthyQuips.Length)]);
    }

    // Stable "random" choice: string.GetHashCode is randomised per process, so use a tiny own hash.
    private static int Pick(string s, int count)
    {
        uint h = 2166136261;
        foreach (char c in s)
            h = (h ^ c) * 16777619;
        return (int)(h % (uint)count);
    }
}
