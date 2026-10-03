using System;
using System.Threading;
using Robust.Shared.IoC;
using Robust.Shared.Utility;

namespace Robust.Client.ResourceManagement;

/// <summary>
///     Base class for all resource types managed by the client resource cache (textures, fonts, RSIs, audio,
///     shader sources and so on).
/// </summary>
/// <remarks>
///     <para>
///         The cache only accepts resource types that have a public parameterless constructor, so derived classes
///         must keep one and this class declares no constructor that takes arguments.
///     </para>
///     <para>
///         Lifecycle, as driven by the cache:
///     </para>
///     <list type="number">
///         <item><description>A fresh instance is created through the parameterless constructor.</description></item>
///         <item>
///             <description>
///                 <see cref="Load"/> is called once. The instance only becomes visible in the cache after this
///                 succeeds. If it throws, the instance is discarded without <see cref="Dispose"/> being called and
///                 <see cref="Fallback"/> may be consulted.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <see cref="Reload"/> may then be called any number of times, always for the same path and never
///                 before a successful load.
///             </description>
///         </item>
///         <item>
///             <description><see cref="Dispose"/> is called when the cache itself is disposed (shutdown).</description>
///         </item>
///     </list>
///     <para>
///         Resources are used from the main/render thread; no thread safety is required of derived classes.
///     </para>
/// </remarks>
public abstract class BaseResource : IDisposable
{
    /// <summary>
    ///     Path of a substitute resource of the same type, to use when this resource cannot be loaded from the
    ///     requested path. The default is <see langword="null"/>, meaning there is no fallback and load failures
    ///     propagate to the caller.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The cache reads this from an instance that may be freshly constructed, never loaded, or whose
    ///         <see cref="Load"/> just failed. Overrides must therefore return a constant and must not depend on any
    ///         state set by <see cref="Load"/>. The getter should be cheap and free of side effects.
    ///     </para>
    ///     <para>
    ///         If loading throws and fallbacks are enabled for that request, the cache logs the failure and loads
    ///         this path as the same type with fallbacks disabled, so a fallback is never chained. If that load also
    ///         fails, the exception reaches the caller. When this property is <see langword="null"/>, the original
    ///         exception is logged and rethrown. Asking the cache directly for the fallback of a type that has none
    ///         is an error.
    ///     </para>
    /// </remarks>
    public virtual ResPath? Fallback => null;

    /// <summary>
    ///     Releases native, GPU or audio handles owned by this resource. The base implementation does nothing, so
    ///     overrides do not have to call it, although doing so is harmless.
    /// </summary>
    /// <remarks>
    ///     The cache calls this once for every cached instance when the cache is disposed, normally on the main
    ///     thread during shutdown. It is not called when a resource is merely reloaded. Overrides should tolerate
    ///     being called more than once. This class has no <c>Dispose(bool)</c> pattern and no finalizer; derived
    ///     classes override this method directly.
    /// </remarks>
    public virtual void Dispose()
    {
    }

    /// <summary>
    ///     Populates this instance from the virtual file system at <paramref name="path"/>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The cache calls this exactly once on a fresh instance, before the instance becomes visible. Services
    ///         the resource needs (resource manager, graphics, audio, font manager and so on) are resolved from
    ///         <paramref name="dependencies"/>. A resource type may also call its own <see cref="Load"/> internally,
    ///         for example from its <see cref="Reload"/> implementation.
    ///     </para>
    ///     <para>
    ///         On failure, implementations must throw. The cache treats a <see cref="System.IO.FileNotFoundException"/>
    ///         specially: it means the resource does not exist, and the path is remembered as missing so that
    ///         later try-get calls do not retry. Any other exception is logged and either makes try-get return
    ///         <see langword="false"/> or triggers the <see cref="Fallback"/> logic or a rethrow. Because the cache
    ///         discards a failed instance without calling <see cref="Dispose"/>, implementations should release any
    ///         partially acquired state themselves before throwing.
    ///     </para>
    /// </remarks>
    /// <param name="dependencies">The dependency collection to resolve services from.</param>
    /// <param name="path">Rooted path of the resource, for example <c>/Textures/foo.png</c>.</param>
    public abstract void Load(IDependencyCollection dependencies, ResPath path);

    /// <summary>
    ///     Re-reads the resource from <paramref name="path"/> after the underlying file changed (hot reload) and
    ///     updates this instance in place, so existing references see the new data.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The base implementation does nothing, so types without hot-reload support silently ignore reload
    ///         requests. The cache only calls this for an instance already cached under that exact type and path;
    ///         otherwise the request is ignored. Exceptions thrown here are logged by the cache and rethrown to its
    ///         caller.
    ///     </para>
    ///     <para>
    ///         The cache's reload entry point does not currently supply a token, so implementations normally
    ///         receive <c>default</c>, which can never be cancelled. Implementations that retry on transient
    ///         "file in use" errors may substitute their own timeout in that case, and should throw
    ///         <see cref="OperationCanceledException"/> when the token is cancelled or that timeout expires.
    ///         Implementations with no waiting behaviour can ignore it.
    ///     </para>
    /// </remarks>
    /// <param name="dependencies">The dependency collection to resolve services from.</param>
    /// <param name="path">The same path that was passed to <see cref="Load"/>.</param>
    /// <param name="ct">Optional cancellation token for the reload; defaults to no cancellation.</param>
    public virtual void Reload(IDependencyCollection dependencies, ResPath path, CancellationToken ct = default)
    {
    }
}
