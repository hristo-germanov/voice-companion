namespace ChatGPTVoiceCompanion.Services;

internal static class EdgeSpeechText
{
    public static List<string> Split(string text)
    {
        // Remove formatting-only lines without stripping sentence punctuation.
        var readable = string.Join('\n', text.Replace("\r\n", "\n").Split('\n')
            .Where(line => string.IsNullOrWhiteSpace(line) || line.Any(char.IsLetterOrDigit)))
            .Trim();
        if (!readable.Any(char.IsLetterOrDigit)) return [];
        if (readable.Length <= 1000) return [readable];

        var chunks = new List<string>();
        var offset = 0;
        while (offset < readable.Length)
        {
            // A small first chunk starts sooner; later chunks are prefetched during playback.
            var limit = chunks.Count == 0 ? 400 : 1000;
            var end = Math.Min(readable.Length, offset + limit);
            if (end < readable.Length)
            {
                var boundary = end;
                while (boundary > offset + limit / 2 && !char.IsWhiteSpace(readable[boundary - 1])) boundary--;
                if (boundary > offset + limit / 2) end = boundary;
                if (end > offset && char.IsHighSurrogate(readable[end - 1])) end--;
            }
            chunks.Add(readable[offset..end]);
            offset = end;
        }
        return chunks;
    }
}
