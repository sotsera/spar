import { actions, menus } from "@spar/app/sdk";

declare function copySelectedRow(): void;
declare function openSearch(): void;
declare function refresh(): void;
declare function redoLast(): void;

export const appActions = actions.define({
  copyRow: { label: "Copy row", shortcut: "Ctrl+C", run: () => copySelectedRow() },
  findTable: { label: "Find table…", shortcut: "Ctrl+K", run: () => openSearch() },
  // A different shortcut per OS, following each platform's convention.
  redo: { label: "Redo", shortcut: { windows: "Ctrl+Y", linux: "Ctrl+Shift+Z" }, run: () => redoLast() },
});

export function attachGridMenu(grid: HTMLElement): void {
  grid.addEventListener("contextmenu", async e => {
    e.preventDefault();
    // A native popup at the pointer: actions, a separator, and an inline item.
    await menus.show(e, [appActions.copyRow, appActions.findTable, "-", { label: "Refresh", run: refresh }]).match({
      success: () => {},
      errors: { dismissed: () => {} },
    });
  });
}
