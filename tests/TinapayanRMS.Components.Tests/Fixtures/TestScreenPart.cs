using System.Reactive.Subjects;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

// In the ViewModels' namespace, as a screen's parts are: ScreenBase takes a get-only property of a type from there for a
// part, and watches its properties, lists and commands too.
namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>A part of a test screen's ViewModel, as the Products screen's Packaging tab is: a command of its own.</summary>
public sealed class TestScreenPart : ReactiveObject
{
    public TestScreenPart()
    {
        RemoveCommand = ReactiveCommand.Create(() => { }, CanRemove);
    }

    public BehaviorSubject<bool> CanRemove { get; } = new(false);

    public ReactiveCommand<RxVoid, RxVoid> RemoveCommand { get; }
}
