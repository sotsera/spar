export function AppTitleBar({ onSearch }: { readonly onSearch: (text: string) => void }) {
  return (
    // The whole header drags the window; buttons, links and inputs inside stay clickable.
    <header data-spar-titlebar className="titlebar">
      <span>SQL Tool</span>
      <input type="search" aria-label="Search connections" onChange={e => onSearch(e.target.value)} />
      {/* Empty: the SDK adds minimise, maximise and close, styled for the OS and themed by CSS variables. */}
      <div data-spar-caption />
    </header>
  );
}
