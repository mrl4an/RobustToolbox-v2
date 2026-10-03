namespace Robust.Shared.ContentPack
{
    /// <summary>
    ///     Base class for the entry point of a server-side content assembly.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A server content assembly declares one subclass and overrides the lifecycle methods
    ///         inherited from <see cref="GameShared"/> (<c>PreInit</c>, <c>Init</c>, <c>PostInit</c>,
    ///         <c>Update</c> and <c>Shutdown</c>) to register server-side dependencies, initialize
    ///         managers and systems, and clean up on shutdown. Subclasses need a parameterless
    ///         constructor, and may use dependency-injected fields because the engine populates the
    ///         instance after creating it.
    ///     </para>
    ///     <para>
    ///         This type adds nothing of its own. It is the server-side counterpart of
    ///         <see cref="GameClient"/> and exists to state intent. The engine does not look for it
    ///         specifically: it instantiates every type in a loaded content assembly that derives from
    ///         <see cref="GameShared"/>, and which assemblies are loaded is decided elsewhere.
    ///     </para>
    /// </remarks>
    public abstract class GameServer : GameShared
    {
    }
}
