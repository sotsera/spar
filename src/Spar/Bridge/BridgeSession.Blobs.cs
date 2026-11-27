using System.Text.Json;
using Spar.Runtime;

namespace Spar.Bridge;

internal sealed partial class BridgeSession
{
    /// <summary>
    /// <c>spar.releaseBlob</c>: the page's <c>release()</c> of a handle. Releasing one that is gone already succeeds, so a
    /// page that releases twice, or after its document's other handles were dropped, sees no error.
    /// </summary>
    private SparOutcome ReleaseBlob(ReadOnlyMemory<byte> request)
    {
        string id;
        try
        {
            id = SparJson.Read(request, SparJson.ReadString);
        }
        catch (JsonException exception)
        {
            return SparOutcome.FromFailure(SparFailure.InvalidRequest($"releaseBlob takes a handle's id: {exception.Message}"));
        }

        _blobs.Release(id);
        return SparOutcome.FromValue(ReadOnlyMemory<byte>.Empty);
    }
}
