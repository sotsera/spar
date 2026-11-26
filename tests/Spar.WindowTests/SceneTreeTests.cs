using System.Text;
using System.Text.Json;
using Spar.Testing;
using WindowHost;
using static Spar.Testing.SparAssertions;

namespace Spar.WindowTests;

/// <summary>
/// The page engine's tree, as <c>spar snap</c> writes it to <c>tree.json</c> and <c>spar tree</c> reads it: an element's
/// text where its name does not show it (a status line, a paragraph, a list item), once and in its sentence, without
/// the text that names another element, hidden text or a password.
/// </summary>
[Trait("Surface", "Window")]
public sealed class SceneTreeTests
{
    private static readonly string Long = string.Concat(Enumerable.Repeat("Lorem ipsum ", 25)).Trim();

    /// <summary>The template app's page after it greeted Ada, and the other shapes of text a page has.</summary>
    [SparScene("tree-text")]
    internal static async Task TreeTextScene(SparScene scene)
    {
        await WindowSupport.SkipUnlessSupportedAsync();
        var app = await scene.StartAsync<Program>();
        await Expect(app.Page.GetByRole(SparRole.Status)).ToHaveTextAsync("Ready");
        await app.Page.EvaluateAsync("""
            () => {
              const long = "Lorem ipsum ".repeat(25).trim();
              const h = (tag, attributes, ...children) => {
                const element = document.createElement(tag);
                for (const [name, value] of Object.entries(attributes ?? {})) {
                  if (name === "value" || name === "hidden") { element[name] = value; } else { element.setAttribute(name, value); }
                }
                element.append(...children);
                return element;
              };
              const unseen = h("span", null, "Unseen");
              unseen.style.visibility = "hidden";
              const table = h("table", null, h("caption", null, "Prices"),
                h("tr", null, h("th", null, "Item"), h("th", null, "Price")),
                h("tr", null, h("td", null, "Tea"), h("td", null, "3")));
              // The test page's own content stays, out of the tree, for its script.
              for (const child of document.body.children) { child.inert = true; child.hidden = true; }
              document.body.append(
                h("main", null,
                  h("h1", null, "MyApp"),
                  h("form", null,
                    h("label", { for: "greet-name" }, "Name"),
                    h("input", { id: "greet-name", value: "Ada" }),
                    h("button", { type: "submit" }, "Greet")),
                  h("p", { role: "status" }, "Hello, Ada!")),
                h("section", { "aria-label": "Notes" },
                  h("p", null, "Read ", h("a", { href: "#docs" }, "the docs"), " before you ", h("strong", null, "start"), "."),
                  h("ul", null, h("li", null, "Buy milk"), h("li", null, "Call ", h("em", null, "Bob"))),
                  h("div", { role: "alert" }, "Disk almost full"),
                  h("div", null, "Loose text in a section"),
                  h("p", null, long),
                  h("button", { "aria-label": "Close" }, "×"),
                  h("span", { hidden: true }, "Secret"),
                  unseen,
                  h("fieldset", null, h("legend", null, "Shipping"),
                    h("label", null, "Password", h("input", { id: "tree-password", type: "password", value: "hunter2" })),
                    h("button", { id: "tree-show", "aria-labelledby": "tree-show tree-password" }, "Show")),
                  table,
                  h("p", { id: "tree-summary" }, "Weekly summary"),
                  h("div", { role: "region", "aria-labelledby": "tree-summary" }, "Body of the summary")));
            }
            """);
    }

    [Fact]
    public async Task The_tree_shows_text_its_names_do_not()
    {
        var capture = await SparScenes.CaptureAsync("tree-text", TestContext.Current.CancellationToken);

        Assert.Equal(
            $"""
            document
              main
                heading "MyApp"
                form
                  textbox "Name" = "Ada"
                  button "Greet"
                status: "Hello, Ada!"
              region "Notes": "Loose text in a section"
                paragraph: "Read the docs before you start."
                  link "the docs"
                  strong
                list
                  listitem: "Buy milk"
                  listitem: "Call Bob"
                    emphasis
                alert: "Disk almost full"
                paragraph: "{Long[..200]} …"
                button "Close"
                group "Shipping"
                  textbox "Password" = "(redacted)"
                  button "Show (redacted)"
                table "Prices"
                  caption: "Prices"
                  row "Item Price"
                    columnheader "Item"
                    columnheader "Price"
                  row "Tea 3"
                    cell "Tea"
                    cell "3"
                paragraph: "Weekly summary"
                region "Weekly summary": "Body of the summary"

            """.ReplaceLineEndings("\n"), Lines(capture.Tree));
        Assert.DoesNotContain("hunter2", capture.Tree, StringComparison.Ordinal);
        Assert.DoesNotContain("Secret", capture.Tree, StringComparison.Ordinal);
        Assert.DoesNotContain("Unseen", capture.Tree, StringComparison.Ordinal);
        Assert.Empty(capture.Problems);
    }

    /// <summary>Each node as a line: its role, name, text and value, indented by its depth.</summary>
    private static string Lines(string tree)
    {
        using var parsed = JsonDocument.Parse(tree);
        var lines = new StringBuilder();
        foreach (var node in parsed.RootElement.GetProperty("nodes").EnumerateArray())
        {
            lines.Append(' ', 2 * node.GetProperty("depth").GetInt32()).Append(node.GetProperty("role").GetString());
            if (node.GetProperty("name").GetString() is { Length: > 0 } name)
            {
                lines.Append($" \"{name}\"");
            }

            if (node.TryGetProperty("text", out var text))
            {
                lines.Append($": \"{text.GetString()}\"");
            }

            if (node.TryGetProperty("value", out var value))
            {
                lines.Append($" = \"{value.GetString()}\"");
            }

            lines.Append('\n');
        }

        return lines.ToString();
    }
}
