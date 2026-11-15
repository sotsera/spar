using Microsoft.Extensions.DependencyInjection;
using SampleApp;
using SampleApp.Notes;
using SampleApp.Orders;
using Spar;

// The decided shape: one Create, one Run. Tests run this same file inside the test process.
var app = SparApplication.Create(args, spar =>
{
    spar.Window(w => w.Titled("Notes").Size(900, 600));

    // A run-time value, as a portable install would set it. A test host moves it under the test's data root.
    spar.Folders(f => f.Logs(Path.Combine(AppContext.BaseDirectory, "logs")));

    spar.Services.AddSingleton(TimeProvider.System);
    spar.Services.AddSingleton<INoteStore, InMemoryNoteStore>();
    spar.Services.AddScoped<NotesApi>();

    // Reaches a real database: a test must replace it, or allow it explicitly.
    spar.Services.AddExternal<IOrderDatabase, SqlOrderDatabase>();
    spar.Services.AddScoped<OrdersApi>();

    // Stand-in for the registration the contract generator emits.
    spar.Contracts(SampleContracts.Catalog);
});

return app.Run();
