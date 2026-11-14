using Microsoft.Data.SqlClient;

namespace Minimal;

internal static class Program
{
    private static int Main()
    {
        using var connection = new SqlConnection(Environment.GetEnvironmentVariable("SPAR_SQL_CS"));
        connection.Open();
        using var command = new SqlCommand("SELECT 1", connection);
        Console.WriteLine($"SELECT 1 -> {command.ExecuteScalar()}");
        return 0;
    }
}
