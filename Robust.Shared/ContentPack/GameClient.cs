namespace Robust.Shared.ContentPack
{
    /// <summary>
    ///     Base class for the entry point of a client-side content assembly.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A client content assembly declares one subclass and overrides the lifecycle methods
    ///         inherited from <see cref="GameShared"/> (<c>PreInit</c>, <c>Init</c>, <c>PostInit</c>,
    ///         <c>Update</c> and <c>Shutdown</c>) to register services, set up systems and load resources.
    ///         Subclasses need a parameterless constructor, and may use dependency-injected fields because
    ///         the engine populates the instance after creating it.
    ///     </para>
    ///     <para>
    ///         This type adds nothing of its own. It is the client-side counterpart of
    ///         <see cref="GameServer"/> and exists to state intent. The engine does not look for it
    ///         specifically: it instantiates every type in a loaded content assembly that derives from
    ///         <see cref="GameShared"/>, and which assemblies are loaded is decided elsewhere.
    ///     </para>
    /// </remarks>
    public abstract class GameClient : GameShared
    {
    }
}
