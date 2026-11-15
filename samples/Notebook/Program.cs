using Microsoft.Extensions.DependencyInjection;
using Notebook.Pages;
using Spar;

var app = SparApplication.Create(args, spar =>
{
    spar.Services.AddSingleton(TimeProvider.System);
    spar.Services.AddSingleton<IPageStore, MarkdownPageStore>();
});

return app.Run();
