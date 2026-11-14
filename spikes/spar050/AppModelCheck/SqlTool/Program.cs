using Microsoft.Extensions.DependencyInjection;
using Spar;
using SqlTool.Connections;

var app = SparApplication.Create(args, spar =>
{
    // Window colours before the page exists, in both themes (plan 3d). Decided.
    spar.Window(w => w.Background(light: "#ffffff", dark: "#1f1f1f"));

    // The strict CSP is the default; each addition is explicit (plan 3b). Decided.
    spar.ContentSecurity(csp => csp.FontSrc("https://fonts.gstatic.com"));

    // Powerful features are denied unless declared (plan 3b). Leaning.
    spar.Permissions(p => p.Allow(Permission.ClipboardRead));

    // Services. Open: Microsoft DI or a generated container (+0.3-0.9 MB in AOT).
    spar.Services.AddSingleton<IConnectionStore, FileConnectionStore>();
    spar.Services.AddScoped<IConnections, ConnectionsApi>();
});

return app.Run();
