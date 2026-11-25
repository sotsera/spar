using Spar.Runtime;

namespace Spar.BuildTests;

/// <summary>
/// What a build of the copy says it was built from (decision "The spar tool (implementation record; choices for review;
/// 2026-11-22)"): the copy sits in the repository's ignored <c>artifacts/</c>, so its builds see the repository's git
/// work tree: its HEAD, and uncommitted changes when <c>git status --porcelain</c> lists any (the copy's own builds
/// write only ignored files, so the state is the same as when they built).
/// </summary>
internal sealed record BuildIdentityExpectation(string Variant, string Revision, bool Dirty)
{
    public static async Task<BuildIdentityExpectation> OfAsync(Workspace workspace, string variant)
    {
        var head = await Tool.RunAsync(workspace.Repository, "git", "rev-parse", "HEAD");
        Assert.True(head.Exit == 0, head.Output);
        var status = await Tool.RunAsync(workspace.Repository, "git", "--no-optional-locks", "status", "--porcelain");
        Assert.True(status.Exit == 0, status.Output);
        return new BuildIdentityExpectation(variant, head.Output.Trim(), status.Output.Trim().Length > 0);
    }

    public void Check(BuildIdentity? identity)
    {
        Assert.NotNull(identity);
        Assert.Equal(Variant, identity.Variant);
        Assert.Equal(Revision, identity.Revision);
        Assert.Equal(Dirty, identity.Dirty);
        Assert.Matches("^[0-9a-f]{16}$", identity.Fingerprint);
    }
}
