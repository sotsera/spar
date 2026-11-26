#!/usr/bin/env bash
# A reference solution: the command, the hook's call and the button.
source "$EVAL_LIB"

replace Notebook/Pages/PagesService.cs '    [SparCommand]
    public async Task<Result<PageError>> DeleteAsync(' '    [SparCommand]
    public async Task<Result<Page, PageError>> DuplicateAsync(PageRef request, CancellationToken cancellationToken)
    {
        var original = await store.FindAsync(request.Id, cancellationToken);
        if (original is null)
        {
            return new PageError.NotFound(request.Id);
        }

        return await CreateAsync(new CreatePage($"{original.Title} (copy)", original.Markdown), cancellationToken);
    }

    [SparCommand]
    public async Task<Result<PageError>> DeleteAsync('

replace Notebook/frontend/src/notebook.ts '  remove(): Promise<void>;' '  remove(): Promise<void>;
  duplicate(): Promise<void>;'

replace Notebook/frontend/src/notebook.ts '  const remove = useCallback(async () => {' '  const duplicate = useCallback(async () => {
    const state = currentRef.current;
    if (state.kind !== "open") {
      return;
    }

    await pages.duplicate({ id: state.page.id }).match({
      success: page => {
        newestDraft.current = page.markdown;
        setCurrent({ kind: "open", page, draft: page.markdown, save: { kind: "saved" } });
      },
      errors: { notFound: () => undefined, titleTaken: () => undefined, invalidTitle: () => undefined, changedElsewhere: () => undefined },
    });
    await refresh();
  }, [refresh]);

  const remove = useCallback(async () => {'

replace Notebook/frontend/src/notebook.ts '    open,
    close: () => setCurrent({ kind: "none" }),' '    open,
    duplicate,
    close: () => setCurrent({ kind: "none" }),'

replace Notebook/frontend/src/components/PageView.tsx '  readonly onDelete: () => void;
}' '  readonly onDelete: () => void;
  readonly onDuplicate: () => void;
}'

replace Notebook/frontend/src/components/PageView.tsx 'export function PageView({ page, draft, save, onEdit, onReload, onKeepMine, onDelete }: PageViewProps) {' 'export function PageView({ page, draft, save, onEdit, onReload, onKeepMine, onDelete, onDuplicate }: PageViewProps) {'

replace Notebook/frontend/src/components/PageView.tsx '        <ModeSwitch mode={mode} onMode={setMode} />' '        <ModeSwitch mode={mode} onMode={setMode} />
        <button type="button" onClick={onDuplicate} className="rounded-md px-2 py-1 text-sm hover:bg-line/60 dark:hover:bg-line-dark/60">
          Duplicate
        </button>'

replace Notebook/frontend/src/App.tsx '              onDelete={() => void notebook.remove()}' '              onDelete={() => void notebook.remove()}
              onDuplicate={() => void notebook.duplicate()}'
