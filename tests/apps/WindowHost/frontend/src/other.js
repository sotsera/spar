// The second page: it echoes, so a test can see the new document's calls work after a navigation.
import "./styles.css";
import { connectSpar, hostChannel } from "@spar/app/sdk";
import { fingerprint } from "@spar/app/spar";
import { host } from "@spar/app/host";

const connection = connectSpar(hostChannel(), fingerprint);
const status = document.getElementById("status");
document.getElementById("echo").addEventListener("click", () => host.echo("again").match({
  success: value => {
    status.textContent = `Echo: ${value}`;
  },
  failure: error => {
    status.textContent = `Echo: ${error.kind} ${error.code}`;
  },
}));

connection.ready();
status.textContent = "Ready";
