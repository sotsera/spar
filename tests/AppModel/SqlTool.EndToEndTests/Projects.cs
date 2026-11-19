// Stands in for the Projects class Spar.Sdk generates in a test project with <SparBlackBoxTests>true</SparBlackBoxTests>
// (docs/app-model.md 6.13). The real-Spar build in ../RealTests gets the generated one.
internal static class Projects
{
    public static Spar.Testing.SparProject SqlTool { get; } = new("SqlTool", "com.example.SqlTool", "SqlTool.exe", "");
}
