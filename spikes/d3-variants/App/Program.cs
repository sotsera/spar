using D3.App;

Console.WriteLine($"variant: {Variant.Name}");
Console.WriteLine($"notes: {string.Join(", ", Notes.Seed())}");
#if SPAR_VARIANT_DESKTOPTEST
Console.WriteLine(DesktopTestHooks.Describe());
#endif
