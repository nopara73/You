namespace You.Library;

public static class BrowserInputLogic
{
    public static bool UrlTextMatchesTarget(string? text, string targetUrl)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.Trim().ToLowerInvariant();
        normalized = normalized.Replace("https://", string.Empty).Replace("http://", string.Empty);
        normalized = normalized.TrimEnd('/');

        return normalized == targetUrl;
    }

    public static bool TryGetVirtualKeyForCharacter(char character, out byte virtualKey)
    {
        if (character is >= 'a' and <= 'z')
        {
            virtualKey = (byte)char.ToUpperInvariant(character);
            return true;
        }

        if (character is >= 'A' and <= 'Z')
        {
            virtualKey = (byte)character;
            return true;
        }

        if (character == '.')
        {
            virtualKey = 0xBE;
            return true;
        }

        virtualKey = 0;
        return false;
    }

    public static double EaseInOutCubic(double t)
    {
        return t < 0.5
            ? 4.0 * t * t * t
            : 1.0 - Math.Pow(-2.0 * t + 2.0, 3.0) / 2.0;
    }
}
