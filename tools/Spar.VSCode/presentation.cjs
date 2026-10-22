const clean = (value) => String(value || "").replace(/[\x00-\x1f\x7f-\x9f\u2028\u2029]/g, " ").slice(0, 4096);

// Display state only. The coordinator remains the authority for document readiness and ownership.
exports.createPresentation = () => {
  let generation = 0;
  let ready = false;
  let paused = false;
  let phase = "starting";
  return (event) => {
    if (event.generation && event.generation < generation) return undefined;
    if (event.generation > generation) {
      generation = event.generation;
      ready = false;
      paused = false;
      phase = "starting";
    }
    const previous = paused ? "debugger paused" : phase;
    switch (event.kind) {
      case "session-phase":
        ready = false;
        phase = ({ building: "building", "starting-frontend": "starting frontend",
          "launching-application": "launching application", "waiting-debugger": "waiting for debugger",
          "awaiting-readiness": "waiting for application readiness" })[event.code] || "starting";
        break;
      case "application-ready": ready = true; phase = "ready"; break;
      case "application-not-ready":
      case "bridge-connected": ready = false; phase = "waiting for application readiness"; break;
      case "backend-updated": if (ready) phase = "updated"; break;
      case "backend-restarting": ready = false; phase = "retiring backend"; break;
      case "application-stop-requested": ready = false; if (phase !== "stopping") phase = "retiring backend"; break;
      case "backend-stopped": ready = false; phase = "build failed"; break;
      case "debugger-diagnostic": if (ready) phase = "edit rejected"; break;
      case "debugger-state": paused = event.code === "paused"; break;
      case "session-failed":
      case "startup-error": ready = false; paused = false; phase = "failed"; break;
      case "session-stopping": ready = false; paused = false; phase = "stopping"; break;
      case "session-stopped": ready = false; paused = false; phase = "stopped"; break;
    }
    const text = `Spar: ${paused ? "debugger paused" : phase}`;
    const detail = ["session-failed", "backend-stopped", "cleanup-failed", "build-diagnostic",
      "debugger-diagnostic", "startup-error"].includes(event.kind) ? clean(event.detail || event.message) : "";
    return { text, message: detail || (text !== `Spar: ${previous}` ? text : undefined) };
  };
};
