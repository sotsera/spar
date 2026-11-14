using Spar;

var host = Environment.GetEnvironmentVariable("FONT_HOST") ?? "https://fonts.example";
const string Ok = "https://ok.example";

SparApplication.Create(args, spar =>
{
    spar.ContentSecurity(csp => csp.FontSrc(Ok).ImgSrc(host));
    if (args.Length > 0)
    {
        spar.ContentSecurity(csp => csp.ConnectSrc("https://api.example"));
    }

    Helpers.AddCdn(spar);
    spar.ContentSecurity(csp => csp.ScriptSrc("https://a.example; script-src *"));
    Action<ContentSecurityBuilder> configure = csp => csp.ImgSrc("https://img.example");
    spar.ContentSecurity(configure);
});

internal static class Helpers
{
    public static void AddCdn(SparBuilder spar) => spar.ContentSecurity(csp => csp.ScriptSrc("https://cdn.example"));
}
