using Microsoft.Extensions.DependencyInjection;
using Notebook.Pages;
using Spar;

var app = SparApplication.Create(args, spar =>
{
    // A page still being written keeps the window until the user decides (docs/app-model.md 6.11).
    spar.OnCloseRequested((request, services) => services.GetRequiredService<IPageStore>().IsWriting
        ? CloseDecision.Ask("A page is still being saved. Close anyway?")
        : CloseDecision.Close);

    spar.Services.AddSingleton(TimeProvider.System);
    spar.Services.AddSingleton<IPageStore, MarkdownPageStore>();
});

return app.Run();
