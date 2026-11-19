import * as Checkbox from "@radix-ui/react-checkbox";
import * as Dialog from "@radix-ui/react-dialog";
import * as DropdownMenu from "@radix-ui/react-dropdown-menu";
import * as Select from "@radix-ui/react-select";
import { type FormEvent, type MouseEvent, type ReactNode, useEffect, useState } from "react";

// A small orders app with the things window-test journeys touch: labelled native and Radix controls, a menu, a
// listbox, a modal dialog with an entry animation, a list that loads after a delay, a button enabled later, a
// client-side route, a file input, an alert, and console and page errors.

type Order = { id: number; customer: string; total: string };

function usePath(): [string, (path: string) => void] {
  const [path, setPath] = useState(location.pathname);
  useEffect(() => {
    const onPop = () => setPath(location.pathname);
    addEventListener("popstate", onPop);
    return () => removeEventListener("popstate", onPop);
  }, []);
  return [path, (next) => { history.pushState(null, "", next); setPath(next); }];
}

export function App() {
  const [path, go] = usePath();
  const link = (to: string, label: string) => (
    <a
      href={to}
      onClick={(e: MouseEvent) => { e.preventDefault(); go(to); }}
      className={`rounded px-3 py-1.5 text-sm font-medium ${path === to ? "bg-slate-900 text-white" : "text-slate-700 hover:bg-slate-200"}`}
      aria-current={path === to ? "page" : undefined}
    >
      {label}
    </a>
  );
  return (
    <div className="mx-auto max-w-3xl p-6">
      <nav aria-label="Main" className="mb-6 flex gap-2">
        {link("/", "Orders")}
        {link("/settings", "Settings")}
      </nav>
      {path === "/settings" ? <Settings /> : <Orders />}
    </div>
  );
}

function Settings() {
  return (
    <main>
      <h1 className="text-2xl font-semibold">Settings</h1>
      <p className="mt-2 text-slate-600">Theme: System</p>
    </main>
  );
}

const field = "mt-1 block w-full rounded border border-slate-300 bg-white px-3 py-2 text-sm";
const button = "rounded bg-slate-900 px-4 py-2 text-sm font-medium text-white disabled:opacity-40";
const quiet = "rounded border border-slate-300 bg-white px-3 py-2 text-sm font-medium hover:bg-slate-100";

function Orders() {
  const [name, setName] = useState("");
  const [express, setExpress] = useState(false);
  const [gift, setGift] = useState(false);
  const [region, setRegion] = useState("Americas");
  const [priority, setPriority] = useState("Normal");
  const [status, setStatus] = useState("Draft");
  const [confirming, setConfirming] = useState(false);

  const submit = (e: FormEvent) => { e.preventDefault(); setConfirming(true); };
  const summary = `${name} (${express ? "express" : "standard"}${gift ? ", gift" : ""}, ${region}, ${priority})`;

  return (
    <main>
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Orders</h1>
        <ActionsMenu onPick={(action) => setStatus(action === "archive" ? "Draft archived" : action === "clear" ? "Draft cleared" : "Draft duplicated")} />
      </div>
      <p role="status" className="mt-2 text-sm text-slate-600">{status}</p>

      <form aria-label="New order" onSubmit={submit} className="mt-4 grid gap-4 rounded-lg border border-slate-200 bg-white p-4">
        <label className="text-sm font-medium">
          Customer name
          <input className={field} value={name} onChange={(e) => setName(e.target.value)} autoComplete="off" />
        </label>
        <div className="flex gap-6">
          <label className="flex items-center gap-2 text-sm">
            <input type="checkbox" checked={express} onChange={(e) => setExpress(e.target.checked)} />
            Express delivery
          </label>
          <div className="flex items-center gap-2 text-sm">
            <Checkbox.Root id="gift" checked={gift} onCheckedChange={(v) => setGift(v === true)}
              className="flex h-4 w-4 items-center justify-center rounded border border-slate-400 bg-white">
              <Checkbox.Indicator className="h-2 w-2 rounded-sm bg-slate-900" />
            </Checkbox.Root>
            <label htmlFor="gift">Gift wrap</label>
          </div>
        </div>
        <div className="grid grid-cols-2 gap-4">
          <label className="text-sm font-medium">
            Region
            <select className={field} value={region} onChange={(e) => setRegion(e.target.value)}>
              <option>Americas</option>
              <option>Europe</option>
              <option>Asia</option>
            </select>
          </label>
          <div className="text-sm font-medium">
            <label htmlFor="priority">Priority</label>
            <Select.Root value={priority} onValueChange={setPriority}>
              <Select.Trigger id="priority" className={`${field} flex items-center justify-between`}>
                <Select.Value />
                <Select.Icon>▾</Select.Icon>
              </Select.Trigger>
              <Select.Portal>
                <Select.Content position="popper" sideOffset={4} className="z-50 rounded border border-slate-200 bg-white p-1 shadow-lg">
                  <Select.Viewport>
                    {["Low", "Normal", "High"].map((p) => (
                      <Select.Item key={p} value={p} className="cursor-default rounded px-3 py-1.5 text-sm outline-none data-[highlighted]:bg-slate-100">
                        <Select.ItemText>{p}</Select.ItemText>
                      </Select.Item>
                    ))}
                  </Select.Viewport>
                </Select.Content>
              </Select.Portal>
            </Select.Root>
          </div>
        </div>
        <label className="text-sm font-medium">
          Notes
          <textarea className={field} rows={2} />
        </label>
        <label className="text-sm font-medium">
          Attachment
          <input type="file" className="mt-1 block text-sm" onChange={(e) => setStatus(`Attached ${e.target.files?.length ?? 0} file(s)`)} />
        </label>
        <div>
          <button type="submit" className={button} disabled={name.trim() === ""}>Place order</button>
        </div>
      </form>

      <ConfirmDialog open={confirming} summary={summary} onClose={(placed) => {
        setConfirming(false);
        if (placed) setStatus(`Order placed for ${summary}`);
      }} />

      <RecentOrders />

      <div className="mt-6 flex gap-2">
        <button type="button" className={quiet} onClick={() => { alert("Saved!"); setStatus("Alert closed"); }}>Show alert</button>
        <button type="button" className={quiet} onClick={() => {
          console.error("export failed: disk full");
          setTimeout(() => { throw new Error("Unhandled boom"); });
        }}>Log error</button>
      </div>
    </main>
  );
}

function ActionsMenu({ onPick }: { onPick: (action: string) => void }) {
  const item = "cursor-default rounded px-3 py-1.5 text-sm outline-none data-[highlighted]:bg-slate-100";
  return (
    <DropdownMenu.Root>
      <DropdownMenu.Trigger className={quiet}>Actions</DropdownMenu.Trigger>
      <DropdownMenu.Portal>
        <DropdownMenu.Content align="end" sideOffset={4} className="z-50 min-w-40 rounded border border-slate-200 bg-white p-1 shadow-lg">
          <DropdownMenu.Item className={item} onSelect={() => onPick("duplicate")}>Duplicate draft</DropdownMenu.Item>
          <DropdownMenu.Item className={item} onSelect={() => onPick("archive")}>Archive draft</DropdownMenu.Item>
          <DropdownMenu.Separator className="my-1 h-px bg-slate-200" />
          <DropdownMenu.Item className={item} onSelect={() => onPick("clear")}>Clear draft</DropdownMenu.Item>
        </DropdownMenu.Content>
      </DropdownMenu.Portal>
    </DropdownMenu.Root>
  );
}

function ConfirmDialog({ open, summary, onClose }: { open: boolean; summary: string; onClose: (placed: boolean) => void }) {
  return (
    <Dialog.Root open={open} onOpenChange={(o) => { if (!o) onClose(false); }}>
      <Dialog.Portal>
        <Dialog.Overlay className="dialog-overlay fixed inset-0 bg-black/30" />
        <Dialog.Content className="dialog-content fixed left-1/2 top-1/2 w-96 -translate-x-1/2 -translate-y-1/2 rounded-lg bg-white p-6 shadow-xl">
          <Dialog.Title className="text-lg font-semibold">Confirm order</Dialog.Title>
          <Dialog.Description className="mt-2 text-sm text-slate-600">Place the order for {summary}?</Dialog.Description>
          <div className="mt-6 flex justify-end gap-2">
            <Dialog.Close className={quiet}>Cancel</Dialog.Close>
            <button type="button" className={button} onClick={() => onClose(true)}>Confirm</button>
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}

function RecentOrders() {
  const [orders, setOrders] = useState<Order[] | null>(null);
  const [filter, setFilter] = useState("");
  const [applied, setApplied] = useState("");
  useEffect(() => {
    let live = true;
    const timer = setTimeout(async () => {
      const response = await fetch("/api/orders.json");
      const loaded = (await response.json()) as Order[];
      if (live) setOrders(loaded);
    }, 600);
    return () => { live = false; clearTimeout(timer); };
  }, []);
  const shown = (orders ?? []).filter((o) => o.customer.toLowerCase().includes(applied.toLowerCase()));
  return (
    <section className="mt-8" aria-labelledby="recent">
      <div className="flex items-center justify-between">
        <h2 id="recent" className="text-lg font-semibold">Recent orders</h2>
        <button type="button" className={quiet} disabled={orders === null} data-testid="export">Export</button>
      </div>
      <input type="search" aria-label="Filter orders" className={field} value={filter}
        onChange={(e) => setFilter(e.target.value)}
        onKeyDown={(e) => { if (e.key === "Enter") setApplied(filter); }} />
      {orders === null ? (
        <p className="mt-3 text-sm text-slate-500">Loading orders…</p>
      ) : (
        <ul aria-label="Recent orders" className="mt-3 divide-y divide-slate-200 rounded border border-slate-200 bg-white">
          {shown.map((o) => <Row key={o.id}>#{o.id} {o.customer} — {o.total}</Row>)}
        </ul>
      )}
    </section>
  );
}

function Row({ children }: { children: ReactNode }) {
  return <li className="px-3 py-2 text-sm">{children}</li>;
}
