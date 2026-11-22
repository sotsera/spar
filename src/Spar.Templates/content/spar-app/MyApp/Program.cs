using Spar;

var app = SparApplication.Create(args, spar =>
{
    // The page's own background colours (frontend/src/styles.css), so the window shows them from its first frame.
    spar.Window(w => w
        .Background(light: "#ffffff", dark: "#1d1f24")
        .Size(900, 600));
});

return app.Run();
