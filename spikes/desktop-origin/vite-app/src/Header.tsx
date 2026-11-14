import { useState } from "react";

export function Header({ count }: { readonly count: number | null }) {
  // Spike: the mount time shows whether Fast Refresh kept this component's state.
  const [mounted] = useState(() => Date.now());
  return (
    <header data-mounted={mounted}>
      <h1>Notes 0</h1>
      <p aria-label="Note count">{count === null ? "…" : `${count} notes`}</p>
    </header>
  );
}
