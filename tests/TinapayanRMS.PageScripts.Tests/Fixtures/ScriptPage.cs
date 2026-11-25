using System.Text.Json;
using Microsoft.Playwright;
using TinapayanRMS.Components.Vim;

namespace TinapayanRMS.PageScripts.Tests.Fixtures;

/// <summary>
/// A page of its own, in a browser context of its own, holding the markup it's opened with (shaped as the components
/// draw it) with the app's own <c>app.js</c> and <c>vim.js</c> loaded over it, as the host page loads them. What the
/// scripts call back into C# goes to <c>window.dotNet</c>, a stand-in that records each call (<see cref="CallsAsync"/>) and
/// answers as <c>window.dotNetAnswers</c> says, by the method's name.
/// </summary>
public sealed class ScriptPage : IAsyncDisposable
{
    /// <summary>The C# side of the interop, in the page: each call recorded, each answer looked up by the method's name.</summary>
    private const string DotNetStandIn =
        """
        window.dotNetCalls = [];
        window.dotNetAnswers = {};
        window.dotNet = {
            invokeMethodAsync(name, ...args) {
                window.dotNetCalls.push({ name, args });
                const answer = window.dotNetAnswers[name];
                return Promise.resolve(typeof answer === "function" ? answer(...args) : answer);
            },
        };
        """;

    private static readonly JsonSerializerOptions Interop = new(JsonSerializerDefaults.Web);

    private readonly IBrowserContext _context;

    /// <summary>What the page threw that nothing caught: a script's error, failing the test as it's closed.</summary>
    private readonly List<string> _errors;

    private ScriptPage(IBrowserContext context, IPage page, List<string> errors)
    {
        _context = context;
        Page = page;
        _errors = errors;
    }

    public IPage Page { get; }

    public IKeyboard Keyboard => Page.Keyboard;

    public static async Task<ScriptPage> OpenAsync(EdgeBrowser edge, string body)
    {
        var context = await edge.Browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 1200, Height = 900 } });
        try
        {
            var page = await context.NewPageAsync();
            List<string> errors = [];
            page.PageError += (_, error) =>
            {
                lock (errors)
                {
                    errors.Add(error);
                }
            };
            await page.SetContentAsync($"<!doctype html><html><head><meta charset=\"utf-8\"></head><body>{body}</body></html>");
            await page.AddScriptTagAsync(new PageAddScriptTagOptions { Content = DotNetStandIn });
            await page.AddScriptTagAsync(new PageAddScriptTagOptions { Path = ScriptPath("app.js") });
            await page.AddScriptTagAsync(new PageAddScriptTagOptions { Path = ScriptPath("vim.js") });

            // A script that threw as it ran (a syntax error, a name both declare at the top) is said here, rather than as
            // whatever its absence makes a test fail on.
            if (!await page.EvaluateAsync<bool>("typeof window.tinapayan === 'object' && typeof window.tinapayanVim === 'object'"))
            {
                throw new InvalidOperationException($"app.js or vim.js didn't load: {string.Join(" | ", errors)}");
            }

            return new ScriptPage(context, page, errors);
        }
        catch
        {
            await context.CloseAsync();
            throw;
        }
    }

    /// <summary>Turns the Vim keys on with the app's own key table and command list, as the VimKeys component does.</summary>
    public async Task EnableVimKeysAsync()
    {
        var keys = JsonSerializer.Serialize(VimKeymap.ForPage(), Interop);
        var commands = JsonSerializer.Serialize(VimCommandLine.ForPage(), Interop);
        await Page.EvaluateAsync($"tinapayanVim.enable(window.dotNet, {keys}, {commands})");
    }

    /// <summary>The names of the C# methods the scripts have called so far, in order.</summary>
    public async Task<IReadOnlyList<string>> CallsAsync() => await Page.EvaluateAsync<string[]>("window.dotNetCalls.map(call => call.name)");

    public Task<T> EvaluateAsync<T>(string expression, object? argument = null) => Page.EvaluateAsync<T>(expression, argument);

    public Task EvaluateAsync(string expression, object? argument = null) => Page.EvaluateAsync(expression, argument);

    /// <summary>The id of the element with the cursor, or "" when it has none.</summary>
    public Task<string> FocusedIdAsync() => Page.EvaluateAsync<string>("document.activeElement?.id ?? ''");

    public Task<string> ValueOfAsync(string selector) => Page.InputValueAsync(selector);

    /// <summary>Closes the page, and fails the test if the page threw anything nothing caught while it ran.</summary>
    public async ValueTask DisposeAsync()
    {
        await _context.CloseAsync();
        lock (_errors)
        {
            if (_errors.Count > 0)
            {
                throw new InvalidOperationException($"The page threw: {string.Join(" | ", _errors)}");
            }
        }
    }

    private static string ScriptPath(string name) => Path.Combine(AppContext.BaseDirectory, "Scripts", name);
}
