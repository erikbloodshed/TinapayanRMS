using System.Collections;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Reflection;
using DynamicData;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// Merges freshly loaded rows into a screen's existing lists instead of replacing them, so a tab
/// coming back into view keeps its state (PLAN.md): a row that hasn't changed keeps its object,
/// which is what an SfDataGrid's selection and scroll position and a ComboBoxAdv's SelectedItem
/// are tied to. Only rows whose values changed are swapped for their fresh copy.
/// </summary>
public static class CollectionMerge
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> ScalarProperties = new();
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> ReferenceProperties = new();
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> CollectionProperties = new();

    /// <summary>
    /// Whether two copies of a row hold the same values: every scalar property (numbers, text,
    /// enums, dates), plus the scalar properties of each entity it references, one level deep, so
    /// a renamed category shows up as a change to the ingredient that displays it. A collection the
    /// row owns (an ingredient's Units) counts as part of the row: its elements are compared in
    /// order, each the same way as the row itself, so an added unit or a renamed unit of measure
    /// shows up as a change to the ingredient too.
    /// </summary>
    public static bool HasSameValues(object? a, object? b) => HasSameValues(a, b, referenceDepth: 1);

    /// <summary>Brings the cache in line with <paramref name="fresh"/>: adds new rows, removes gone ones, and replaces only rows whose values changed.</summary>
    public static void MergeFrom<T>(this SourceCache<T, int> cache, IEnumerable<T> fresh)
        where T : notnull =>
        cache.EditDiff(fresh, (current, previous) => HasSameValues(current, previous));

    /// <summary>
    /// Makes <paramref name="list"/> hold <paramref name="fresh"/>, in its order, while keeping the
    /// existing object for every row whose values didn't change.
    /// </summary>
    public static void MergeFrom<T>(this ObservableCollection<T> list, IReadOnlyList<T> fresh, Func<T, int> key)
    {
        for (var i = 0; i < fresh.Count; i++)
        {
            var target = fresh[i];
            var targetKey = key(target);

            var existingIndex = -1;
            for (var j = i; j < list.Count; j++)
            {
                if (key(list[j]) == targetKey)
                {
                    existingIndex = j;
                    break;
                }
            }

            if (existingIndex < 0)
            {
                list.Insert(i, target);
                continue;
            }

            if (existingIndex != i)
            {
                list.Move(existingIndex, i);
            }

            if (!HasSameValues(list[i], target))
            {
                list[i] = target;
            }
        }

        // The first fresh.Count rows now match fresh; anything after them isn't in it any more.
        while (list.Count > fresh.Count)
        {
            list.RemoveAt(list.Count - 1);
        }
    }

    private static bool HasSameValues(object? a, object? b, int referenceDepth)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null || b is null || a.GetType() != b.GetType())
        {
            return false;
        }

        var type = a.GetType();
        foreach (var property in ScalarProperties.GetOrAdd(type, FindScalarProperties))
        {
            if (!Equals(property.GetValue(a), property.GetValue(b)))
            {
                return false;
            }
        }

        if (referenceDepth > 0)
        {
            foreach (var property in ReferenceProperties.GetOrAdd(type, FindReferenceProperties))
            {
                if (!HasSameValues(property.GetValue(a), property.GetValue(b), referenceDepth - 1))
                {
                    return false;
                }
            }
        }

        foreach (var property in CollectionProperties.GetOrAdd(type, FindCollectionProperties))
        {
            if (!HaveSameElements((IEnumerable?)property.GetValue(a), (IEnumerable?)property.GetValue(b), referenceDepth))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HaveSameElements(IEnumerable? a, IEnumerable? b, int referenceDepth)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        var left = a.GetEnumerator();
        var right = b.GetEnumerator();
        while (true)
        {
            var hasLeft = left.MoveNext();
            if (hasLeft != right.MoveNext())
            {
                return false;
            }

            if (!hasLeft)
            {
                return true;
            }

            if (!HasSameValues(left.Current, right.Current, referenceDepth))
            {
                return false;
            }
        }
    }

    private static PropertyInfo[] FindScalarProperties(Type type) =>
        [.. ReadableProperties(type).Where(p => IsScalar(p.PropertyType))];

    private static PropertyInfo[] FindReferenceProperties(Type type) =>
        [.. ReadableProperties(type).Where(p => !IsScalar(p.PropertyType) && p.PropertyType.IsClass && !typeof(IEnumerable).IsAssignableFrom(p.PropertyType))];

    private static PropertyInfo[] FindCollectionProperties(Type type) =>
        [.. ReadableProperties(type).Where(p => !IsScalar(p.PropertyType) && typeof(IEnumerable).IsAssignableFrom(p.PropertyType))];

    private static IEnumerable<PropertyInfo> ReadableProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.GetIndexParameters().Length == 0);

    private static bool IsScalar(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying.IsPrimitive
            || underlying.IsEnum
            || underlying == typeof(string)
            || underlying == typeof(decimal)
            || underlying == typeof(DateTime)
            || underlying == typeof(DateTimeOffset)
            || underlying == typeof(DateOnly)
            || underlying == typeof(TimeOnly)
            || underlying == typeof(TimeSpan)
            || underlying == typeof(Guid);
    }
}
