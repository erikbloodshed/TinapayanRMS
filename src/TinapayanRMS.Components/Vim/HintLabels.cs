namespace TinapayanRMS.Components.Vim;

/// <summary>
/// The labels link hints put on what can be clicked: as short as the count allows, made from the home row first, and
/// none the start of another, so typing a whole label always picks one target. Vimium's way of making them: take the
/// shortest label still free and swap it for itself with each letter in front, until there are enough.
/// </summary>
public static class HintLabels
{
    /// <summary>The letters labels are made of, the easiest to reach first.</summary>
    public const string Alphabet = "sadfjklewcmpgh";

    /// <summary><paramref name="count"/> labels, in lower case.</summary>
    public static IReadOnlyList<string> For(int count)
    {
        if (count <= 0)
        {
            return [];
        }

        var labels = new List<string> { string.Empty };
        var offset = 0;
        while (labels.Count - offset < count || labels.Count == 1)
        {
            var label = labels[offset++];
            foreach (var letter in Alphabet)
            {
                labels.Add(letter + label);
            }
        }

        // Built back to front, so each one taken is a suffix of the ones made from it; reversed, it's their prefix,
        // and none of those left is the prefix of another. Sorted first, so labels sharing a first letter spread out.
        return labels.GetRange(offset, count)
            .Order(StringComparer.Ordinal)
            .Select(label => string.Create(label.Length, label, static (span, source) =>
            {
                for (var i = 0; i < source.Length; i++)
                {
                    span[i] = source[source.Length - 1 - i];
                }
            }))
            .ToList();
    }
}
