namespace D8.App;

internal static class Page
{
    // The Save handler updates the status after a short delay, standing in for a backend round trip.
    // It records whether the click and the last input were trusted (real input) or synthetic (script).
    public const int SaveDelayMilliseconds = 50;

    public const string Html = """
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <title>D8 automation spike</title>
        <style>
          body { font: 16px system-ui, sans-serif; margin: 24px; background: #f4f6fb; color: #1b1f2a; }
          label { display: block; margin-bottom: 6px; font-weight: 600; }
          input { font: inherit; padding: 6px 8px; width: 280px; }
          button { font: inherit; padding: 6px 16px; margin-left: 8px; }
          #status { margin-top: 16px; padding: 8px; background: #fff; border: 1px solid #c8cedb; min-height: 1.4em; }
        </style>
        </head>
        <body>
          <h1>Profile</h1>
          <label for="name">Name</label>
          <input id="name" type="text" autocomplete="off">
          <button id="save" type="button">Save</button>
          <div id="status" role="status" aria-live="polite">Idle</div>
          <script>
            let saves = 0;
            let lastInputTrusted = null;
            const name = document.getElementById('name');
            const status = document.getElementById('status');
            name.addEventListener('input', e => { lastInputTrusted = e.isTrusted; });
            document.getElementById('save').addEventListener('click', e => {
              const value = name.value;
              const clickTrusted = e.isTrusted;
              const n = ++saves;
              setTimeout(() => {
                status.textContent = `Saved #${n}: ${value} (click ${clickTrusted ? 'trusted' : 'synthetic'}, input ${lastInputTrusted === null ? 'none' : lastInputTrusted ? 'trusted' : 'synthetic'})`;
              }, 50);
            });
          </script>
        </body>
        </html>
        """;
}
