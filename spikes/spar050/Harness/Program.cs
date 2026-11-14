using Spar050.Harness;

return args switch
{
    ["codefix", var spikeRoot] => await CodeFixCheck.RunAsync(spikeRoot),
    ["runtime"] => RuntimeProbes.Run(),
    _ => Usage(),
};

static int Usage()
{
    Console.Error.WriteLine("usage: Spar050.Harness codefix <spike folder> | runtime");
    return 2;
}
