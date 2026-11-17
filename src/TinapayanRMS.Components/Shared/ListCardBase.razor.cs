using Microsoft.AspNetCore.Components;

namespace TinapayanRMS.Components.Shared;

/// <summary>
/// Named a component here, in C#, because Razor's first pass, which finds the components the pages use, gives a .razor
/// file with markup in its @code (RowContent) no base at all: the cards on it then weren't taken for components (RZ10012),
/// and Raw Materials and Sub-Assemblies drew no list.
/// </summary>
public abstract partial class ListCardBase : ComponentBase;
