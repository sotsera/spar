#!/usr/bin/env bash
# A reference solution: the search box after the list, a scene of the sidebar, and its capture.
source "$EVAL_LIB"
search='      <div className="px-3 pb-2">
        <label className="relative block">
          <span className="sr-only">Search pages</span>
          <Search aria-hidden className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted dark:text-muted-dark" />
          <input
            ref={searchRef}
            type="search"
            value={query}
            onChange={event => onQuery(event.target.value)}
            placeholder="Search pages"
            className="w-full rounded-md border border-line bg-sheet py-1.5 pr-2 pl-8 text-sm placeholder:text-muted dark:border-line-dark dark:bg-sheet-dark dark:placeholder:text-muted-dark"
          />
        </label>
      </div>
'
replace Notebook/frontend/src/components/Sidebar.tsx "$search
" ''
replace Notebook/frontend/src/components/Sidebar.tsx '      </nav>
' "      </nav>

$search"

replace Notebook.EndToEndTests/Window/NotebookScenes.cs '    /// <summary>The new-page dialog with a title typed in.</summary>' '    /// <summary>The sidebar with two pages.</summary>
    [SparScene("sidebar")]
    public static async Task Sidebar(SparScene scene)
    {
        var app = await scene.StartAsync<Program>();
        foreach (var title in new[] { "Groceries", "Trip to Turin" })
        {
            await app.Page.GetByRole(SparRole.Button, new() { Name = "New page" }).First.ClickAsync();
            var dialog = app.Page.GetByRole(SparRole.Dialog, new() { Name = "New page" });
            await dialog.GetByLabel("Title").FillAsync(title);
            await dialog.GetByRole(SparRole.Button, new() { Name = "Create page" }).ClickAsync();
        }

        await Expect(app.Page.GetByRole(SparRole.Navigation, new() { Name = "Pages" }).GetByRole(SparRole.Listitem)).ToHaveCountAsync(2);
    }

    /// <summary>The new-page dialog with a title typed in.</summary>'

dotnet spar snap sidebar --project Notebook.EndToEndTests
