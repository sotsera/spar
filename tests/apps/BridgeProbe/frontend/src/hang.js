// A page that hangs in its own start-up code, before it ever connects the SDK: only the host's relay, which answers
// the heartbeat from the document's creation, lets the host see it (plan section 3, "Hung pages").
for (;;) {
  // Endless on purpose.
}
