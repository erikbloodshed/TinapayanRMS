using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace TinapayanRMS.Components.Tests.Fixtures;

/// <summary>
/// A visit to a screen, as the router makes one: the component is shown, then left for another address, which disposes
/// it. What it was shown over (its state, a scoped service that lives as long as the window's page, or the folders
/// and lines that screen state holds) goes on, and must not keep hold of it.
/// </summary>
public static class Visits
{
    /// <summary>
    /// Shows <typeparamref name="TComponent"/>, runs <paramref name="whileShown"/> on it, then leaves it, and keeps only a
    /// weak reference to it. Kept out of the test that calls it, so nothing of the test's keeps the component alive.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static WeakReference ShowAndLeave<TComponent>(
        this BunitContext context,
        Action<ComponentParameterCollectionBuilder<TComponent>>? parameters = null,
        Action<IRenderedComponent<TComponent>>? whileShown = null)
        where TComponent : IComponent
    {
        var page = context.Render<Page>(page => page
            .Add(p => p.Showing, true)
            .AddChildContent<TComponent>(parameters ?? (_ => { })));
        var shown = page.FindComponent<TComponent>();
        whileShown?.Invoke(shown);
        var left = new WeakReference(shown.Instance);

        // Left, then drawn once more, as the window's layout is on the next move: a component keeps the frames of its
        // last drawing until it draws again (ComponentState's next render tree), so until then the page it last showed
        // is held by Blazor itself.
        page.Render(page => page.Add(p => p.Showing, false));
        page.Render(page => page.Add(p => p.Showing, false));
        return left;
    }

    /// <summary>Whether what <paramref name="left"/> refers to has gone, once every collection that can be made has been.</summary>
    public static bool IsCollected(this WeakReference left)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return !left.IsAlive;
    }

    /// <summary>The page the component is shown in while <see cref="Showing"/>, as the router shows the screen at the address.</summary>
    private sealed class Page : ComponentBase
    {
        [Parameter] public bool Showing { get; set; }

        [Parameter] public RenderFragment? ChildContent { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            if (Showing)
            {
                builder.AddContent(0, ChildContent);
            }
        }
    }
}
