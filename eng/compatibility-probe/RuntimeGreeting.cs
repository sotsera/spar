using System.Diagnostics.CodeAnalysis;
using System.Reflection.Emit;

namespace Spar.DynamicDependency;

public static class RuntimeGreeting
{
    [RequiresDynamicCode("RuntimeGreeting builds and executes a DynamicMethod; select explicit CoreCLR delivery or remove this dependency.")]
    public static string Create(string name)
    {
        var method = new DynamicMethod("CreateGreeting", typeof(string), [typeof(string)]);
        var instructions = method.GetILGenerator();
        instructions.Emit(OpCodes.Ldstr, "Hello from generated code, ");
        instructions.Emit(OpCodes.Ldarg_0);
        var concatenate = typeof(string).GetMethod(nameof(string.Concat), [typeof(string), typeof(string)])!;
        instructions.Emit(OpCodes.Call, concatenate);
        instructions.Emit(OpCodes.Ret);

        var greet = method.CreateDelegate<Func<string, string>>();
        return greet(name);
    }
}
