using System.Collections.Frozen;

namespace TinapayanRMS.UI.Vim;

/// <summary>
/// Reads typed characters into Vim actions, one character at a time, holding the first of a two-key sequence
/// (<c>gg</c>, <c>gt</c>, <c>dd</c>, <c>cc</c>) until the second arrives. A second key that makes no sequence drops the
/// first and does nothing, as Vim's own <c>gx</c> does. The keys are <see cref="VimKeymap"/>'s, looked up in frozen
/// tables, so a key costs one lookup and builds nothing.
/// </summary>
public sealed class VimKeySequence
{
    private static readonly FrozenDictionary<int, VimAction> Actions = VimKeymap.Bindings
        .Where(binding => binding.IsTyped)
        .DistinctBy(binding => binding.Keys)
        .ToFrozenDictionary(binding => Code(binding.Keys), binding => binding.Action);

    private static readonly FrozenSet<char> Prefixes = VimKeymap.Bindings
        .Where(binding => binding.Keys.Length == 2)
        .Select(binding => binding.Keys[0])
        .ToFrozenSet();

    /// <summary>The first key of a sequence typed so far, or '\0' when none is.</summary>
    public char Pending { get; private set; }

    /// <summary>The action <paramref name="key"/> completes, or <see cref="VimAction.None"/> when it starts a sequence
    /// (see <see cref="Pending"/>) or means nothing.</summary>
    public VimAction Feed(char key)
    {
        if (Pending != '\0')
        {
            var first = Pending;
            Pending = '\0';
            return Actions.GetValueOrDefault(Pair(first, key));
        }

        if (Prefixes.Contains(key))
        {
            Pending = key;
            return VimAction.None;
        }

        return Actions.GetValueOrDefault(key);
    }

    /// <summary>Drops a half-typed sequence. True when there was one.</summary>
    public bool Clear()
    {
        var had = Pending != '\0';
        Pending = '\0';
        return had;
    }

    private static int Code(string keys) => keys.Length == 1 ? keys[0] : Pair(keys[0], keys[1]);

    private static int Pair(char first, char second) => (first << 16) | second;
}
