namespace Robust.Shared.ContentPack
{
    /// <summary>
    ///     Phases of the content entry point's life cycle.
    /// </summary>
    public enum ModRunLevel: byte
    {
        Error = 0,
        Init = 1,
        PostInit = 2,
        PreInit = 3,
    }
}
