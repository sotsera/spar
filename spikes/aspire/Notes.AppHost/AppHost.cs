var builder = DistributedApplication.CreateBuilder(args);

// The loopback bridge token: generated when not supplied, so the tests can pass their own.
var token = builder.AddParameter("spar-token", () => Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)), secret: true);

// The image is already on this machine; never pull the 2 GB again.
var sql = builder.AddSqlServer("sql")
    .WithImageTag("2022-latest")
    .WithImagePullPolicy(ImagePullPolicy.Missing);
var notes = sql.AddDatabase("notes");

// The Spar app in loopback mode. Aspire assigns the port and the token through the environment and passes the
// connection string (ConnectionStrings__notes) and the OTLP endpoint of its dashboard.
var app = builder.AddProject<Projects.React_App>("notes-app", options => options.ExcludeLaunchProfile = true)
    .WithArgs("--loopback")
    .WithHttpEndpoint(name: "http", env: "SPAR_LOOPBACK_PORT", isProxied: false)
    .WithEnvironment("SPAR_LOOPBACK_TOKEN", token)
    .WithReference(notes)
    .WaitFor(notes);

// Vite, run by Aspire instead of by the app. No npm install: node_modules is already there.
var frontend = builder.AddViteApp("frontend", "../../react/React.App/frontend")
    .WithNpm(install: false)
    .WithEnvironment("SPAR_BACKEND_URL", app.GetEndpoint("http"))
    .WithEnvironment("SPAR_BACKEND_TOKEN", token)
    .WaitFor(app);

app.WithEnvironment("SPAR_DEV_SERVER_URL", frontend.GetEndpoint("http"));

builder.Build().Run();
