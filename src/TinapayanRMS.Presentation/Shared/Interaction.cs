namespace TinapayanRMS.Presentation.Shared;

/// <summary>
/// A question a screen state asks whoever shows it, and waits for the answer to: "Delete Cake Flour?", where to save a
/// workbook, what to name a folder. The screen answers it (a dialog, a file picker) by registering a handler, and the
/// tests answer it the same way, so the screen state never touches a window. The handler registered last is asked first, one
/// that leaves the answer unset passes the question to the one before it, and a question nobody answers is a bug, thrown
/// as one.
/// </summary>
/// <typeparam name="TInput">What's asked.</typeparam>
/// <typeparam name="TOutput">The answer.</typeparam>
public sealed class Interaction<TInput, TOutput>
{
    private readonly List<Func<InteractionContext<TInput, TOutput>, Task>> _handlers = [];
    private readonly Lock _gate = new();

    /// <summary>Answers the question with <paramref name="handler"/> until what this returns is disposed.</summary>
    public IDisposable RegisterHandler(Action<InteractionContext<TInput, TOutput>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RegisterHandler(context =>
        {
            handler(context);
            return Task.CompletedTask;
        });
    }

    /// <summary>Answers the question with <paramref name="handler"/>, which may wait (a dialog), until what this returns is disposed.</summary>
    public IDisposable RegisterHandler(Func<InteractionContext<TInput, TOutput>, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_gate)
        {
            _handlers.Add(handler);
        }

        return new Registration(this, handler);
    }

    /// <summary>
    /// Asks <paramref name="input"/>, the handler registered last first, and returns the first answer given.
    /// </summary>
    /// <exception cref="InvalidOperationException">No handler answered.</exception>
    public async Task<TOutput> Handle(TInput input)
    {
        Func<InteractionContext<TInput, TOutput>, Task>[] handlers;
        lock (_gate)
        {
            handlers = [.. _handlers];
        }

        var context = new InteractionContext<TInput, TOutput>(input);
        for (var i = handlers.Length - 1; i >= 0; i--)
        {
            await handlers[i](context);
            if (context.IsHandled)
            {
                return context.Output;
            }
        }

        throw new InvalidOperationException(
            $"Nothing answered the question ({typeof(TInput).Name} to {typeof(TOutput).Name}): no handler is registered, or none gave an answer.");
    }

    private sealed class Registration(Interaction<TInput, TOutput> owner, Func<InteractionContext<TInput, TOutput>, Task> handler) : IDisposable
    {
        public void Dispose()
        {
            lock (owner._gate)
            {
                owner._handlers.Remove(handler);
            }
        }
    }
}

/// <summary>One asking of an <see cref="Interaction{TInput, TOutput}"/>: the question, and the answer once a handler gives it.</summary>
public sealed class InteractionContext<TInput, TOutput>
{
    private TOutput _output = default!;

    internal InteractionContext(TInput input)
    {
        Input = input;
    }

    /// <summary>What's asked.</summary>
    public TInput Input { get; }

    /// <summary>Whether a handler has answered.</summary>
    public bool IsHandled { get; private set; }

    internal TOutput Output => _output;

    /// <summary>Answers the question. Once only: a second answer is a bug.</summary>
    public void SetOutput(TOutput output)
    {
        if (IsHandled)
        {
            throw new InvalidOperationException("The question has been answered already.");
        }

        _output = output;
        IsHandled = true;
    }
}
