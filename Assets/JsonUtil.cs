using System.Globalization;

public static class JsonUtil
{
    public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Esc(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r");
    }

    public static string ToGroundTruthLabel(string animOrAction) => animOrAction switch
    {
        "SeatedDrinking" => "Drinking",
        "DadReading"     => "Reading",
        "DadCleaning"    => "Cleaning",
        "DadPhone"       => "UsingPhone",
        _                => animOrAction
    };
}