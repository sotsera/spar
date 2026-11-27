namespace Spar.Testing;

/// <summary>A surface whose pages reach the backend over a channel a test can drop and restore (the Browser surface).</summary>
internal interface IChannelSurface
{
    void DropChannel();

    void RestoreChannel();
}
