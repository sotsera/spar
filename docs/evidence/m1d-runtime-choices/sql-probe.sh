#!/usr/bin/env bash
# M1d step 7 (docs/evidence/m1d-runtime-choices.md): writes the SqlClient probe the package tests use
# (tests/Spar.PackageTests/SqlClientTests.cs) into a new app made from the template: a command-line mode of its
# Program.cs that opens a connection to a loopback port where nothing listens and prints what SqlClient threw.
#   sql-probe.sh <app folder> <app name>
set -euo pipefail
dir=$1 name=$2
{ echo "using $name;"; echo "using Spar;"; echo; cat <<'CS'
// The probe (no window): what SqlClient throws for a port where nothing listens.
if (args is ["--sql-probe", var port])
{
    return SqlProbe.Run(port);
}
CS
  tail -n +2 "$dir/Program.cs"; } > "$dir/Program.cs.new" && mv "$dir/Program.cs.new" "$dir/Program.cs"
cat > "$dir/SqlProbe.cs" <<CS
using Microsoft.Data.SqlClient;

namespace $name;

internal static class SqlProbe
{
    public static int Run(string port)
    {
        try
        {
            using var connection = new SqlConnection(\$"Server=tcp:127.0.0.1,{port};Connect Timeout=3;Encrypt=false;Pooling=false");
            connection.Open();
            Console.WriteLine("probe: opened");
            return 1;
        }
        catch (Exception problem)
        {
            Console.WriteLine(\$"probe: {problem.GetType().FullName}: {problem.Message}");
            for (var inner = problem.InnerException; inner is not null; inner = inner.InnerException)
            {
                Console.WriteLine(\$"probe: inner {inner.GetType().FullName}: {inner.Message}");
            }

            return problem is SqlException ? 0 : 2;
        }
    }
}
CS
