using Microsoft.Extensions.DependencyInjection;
using Notebook.Pages;
using Spar;

var app = SparApplication.Create(args, spar =>
{
    // The page's own sheet colours, so the window shows them from its first frame in either theme (plan 3d).
    spar.Window(w => w
        .Background(light: "#ffffff", dark: "#1d1f24")
        .Size(1100, 720)
        .MinSize(640, 420));

    // A page still being written keeps the window until the user decides (docs/app-model.md 6.11).
    spar.OnCloseRequested((request, services) => services.GetRequiredService<IPageStore>().IsWriting
        ? CloseDecision.Ask("A page is still being saved. Close anyway?")
        : CloseDecision.Close);

    spar.Services.AddSingleton(TimeProvider.System);
    spar.Services.AddSingleton<IPageStore, MarkdownPageStore>();
});

return app.Run();
