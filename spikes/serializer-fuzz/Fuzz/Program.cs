using Fuzz;

return args.Length > 0 && args[0] == "worker" ? Worker.Run(args)
    : args.Length > 0 && args[0] == "minimise" ? Minimiser.Minimise(args)
    : args.Length > 0 && args[0] == "describe" ? Minimiser.Describe(args)
    : args.Length > 0 && args[0] == "probe" ? Minimiser.Probe(args)
    : args.Length > 0 && args[0] == "scan" ? Minimiser.Scan(args)
    : await Coordinator.RunAsync(args);
