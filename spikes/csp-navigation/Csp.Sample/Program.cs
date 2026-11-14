using Spar;
using Spar.Generated;

const string Fonts = "https://fonts.gstatic.com";

var app = SparApplication.Create(args, spar =>
{
    spar.ContentSecurity(csp => csp
        .FontSrc(Fonts)
        .StyleSrc("https://fonts.googleapis.com")
        .Allow(Permission.Camera));
});

// The Debug check from plan 3b: the policy the app computes at run time equals the one the build generated.
Console.WriteLine("generated: " + SparSecurityPolicy.ContentSecurityPolicy);
Console.WriteLine("runtime:   " + app.Policy.ContentSecurityPolicy);
Console.WriteLine("permissions: " + SparSecurityPolicy.PermissionsPolicy);
var same = app.Policy.ContentSecurityPolicy == SparSecurityPolicy.ContentSecurityPolicy
    && app.Policy.PermissionsPolicy == SparSecurityPolicy.PermissionsPolicy;
Console.WriteLine("match: " + same);
return same ? 0 : 1;
