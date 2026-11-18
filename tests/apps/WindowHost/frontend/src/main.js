// The window tests' page (tests/Spar.WindowTests): controls for the page engine's rules (roles and names, labels,
// fields of every kind, an element that loads late, a dialog that animates in, a button covered for a moment, one far
// down a scroll container) and the calls of the required host tests.
import "./styles.css";
import { connectSpar, hostChannel } from "@spar/app/sdk";
import { fingerprint } from "@spar/app/spar";
import { host } from "@spar/app/host";

const connection = connectSpar(hostChannel(), fingerprint);
const status = document.getElementById("status");
const show = text => { status.textContent = text; };
const on = (id, handler) => document.getElementById(id).addEventListener("click", handler);

const settle = (label, call) => call.match({
  success: value => show(`${label}: ${value}`),
  failure: error => show(`${label}: ${error.kind} ${error.code}`),
});

on("hold", () => { show("Long call: running"); settle("Long call", host.hold("hold")); });
on("slow", () => { show("Slow call: running"); settle("Slow call", host.slow(800)); });
on("echo", () => settle("Echo", host.echo("hello")));

// The order form: Place order is enabled once there is a name.
const customer = document.getElementById("customer");
const place = document.getElementById("place");
customer.addEventListener("input", () => { place.disabled = customer.value.trim() === ""; });
on("place", () => {
  const region = document.getElementById("region");
  const gift = document.getElementById("gift").checked ? ", gift wrapped" : "";
  const day = document.getElementById("day").value || "any day";
  const quantity = document.getElementById("quantity").value;
  document.getElementById("summary").textContent = `${customer.value} ordered ${quantity} for ${region.value}${gift}, ${day}`;
});

// Orders arrive 600 ms after the click.
on("load", () => {
  setTimeout(() => {
    const list = document.createElement("ul");
    list.setAttribute("aria-label", "Orders");
    for (const name of ["Ada", "Grace", "Linus"]) {
      const item = document.createElement("li");
      item.textContent = `Order for ${name}`;
      list.append(item);
    }

    document.getElementById("orders").replaceChildren(list);
  }, 600);
});

// The dialog slides in over 150 ms; a click during the animation must wait for it to stop.
const dialog = document.getElementById("dialog");
on("open", () => {
  dialog.classList.add("entering");
  dialog.hidden = false;
  requestAnimationFrame(() => requestAnimationFrame(() => dialog.classList.remove("entering")));
});
on("confirm", () => {
  dialog.hidden = true;
  show("Confirmed");
});
on("cancel", () => {
  dialog.hidden = true;
  show("Cancelled");
});

// The covered button is under a "Loading…" layer for its first 400 ms.
setTimeout(() => document.getElementById("overlay").remove(), 400);
on("covered", () => show("Covered button clicked"));

on("later", () => setTimeout(() => {
  const button = document.createElement("button");
  button.type = "button";
  button.textContent = "Late button";
  button.addEventListener("click", () => show("Late button clicked"));
  document.getElementById("late").replaceChildren(button);
}, 300));

on("far", () => show("Far away clicked"));
on("log-error", () => console.error("Something went wrong on purpose"));
on("throw", () => setTimeout(() => {
  throw new Error("Thrown on purpose");
}));
on("alert", () => {
  alert("Saved!");
  show("Alert closed");
});

// Typing "/" anywhere but in a field focuses the customer name, as an app's shortcut would.
addEventListener("keydown", event => {
  const target = event.target;
  const typing = target instanceof HTMLInputElement || target instanceof HTMLTextAreaElement || target?.isContentEditable;
  if (event.key === "/" && !typing) {
    event.preventDefault();
    customer.focus();
  }
});

connection.ready();
show("Ready");
