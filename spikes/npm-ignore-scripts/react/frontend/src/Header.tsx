export function Header({ count }: { readonly count: number | null }) {
  return (
    <header>
      <h1>Notes</h1>
      <p aria-label="Note count">{count === null ? "…" : `${count} notes`}</p>
    </header>
  );
}
