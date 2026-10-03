using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Robust.Client.Audio;
using Robust.Shared.ContentPack;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Utility;

namespace Robust.Client.ResourceManagement;

/// <summary>
/// Handles caching of <see cref="BaseResource"/>
/// </summary>
internal sealed partial class ResourceCache : ResourceManager, IResourceCacheInternal, IDisposable
{
    private readonly Dictionary<Type, TypeData> _cachedResources = new();
    private readonly Dictionary<Type, BaseResource> _fallbacks = new();

    public T GetResource<T>(string path, bool useFallback = true) where T : BaseResource, new()
    {
        return GetResource<T>(new ResPath(path), useFallback);
    }

    /// <summary>
    ///     Returns the resource of type <typeparamref name="T"/> stored under <paramref name="path"/>,
    ///     loading it from disk on first use.
    /// </summary>
    /// <param name="path">Resource path to load.</param>
    /// <param name="useFallback">
    ///     When true and the load fails, the type's fallback resource is returned instead of throwing,
    ///     provided the type defines one.
    /// </param>
    /// <remarks>
    ///     Only successfully loaded instances are cached. A fallback is cached under its own path, never
    ///     under the requested one, so a failing path is retried (and logged) on every call.
    /// </remarks>
    public T GetResource<T>(ResPath path, bool useFallback = true) where T : BaseResource, new()
    {
        var typeData = GetTypeData<T>();
        if (typeData.Resources.TryGetValue(path, out var existing))
        {
            return (T) existing;
        }

        var instance = new T();
        try
        {
            instance.Load(IoCManager.Instance!, path);
            typeData.Resources[path] = instance;
            return instance;
        }
        catch (Exception e)
        {
            var fallbackPath = instance.Fallback;
            if (useFallback && fallbackPath != null)
            {
                Sawmill.Error(
                    $"Exception while loading resource {typeof(T)} at '{path}', resorting to fallback.\n{Environment.StackTrace}\n{e}");
                return GetResource<T>(fallbackPath.Value, false);
            }

            Sawmill.Error(
                $"Exception while loading resource {typeof(T)} at '{path}', no fallback available\n{Environment.StackTrace}\n{e}");
            throw;
        }
    }

    public bool TryGetResource<T>(string path, [NotNullWhen(true)] out T? resource) where T : BaseResource, new()
    {
        return TryGetResource(new ResPath(path), out resource);
    }

    public bool TryGetResource<T>(ResPath path, [NotNullWhen(true)] out T? resource) where T : BaseResource, new()
    {
        var cache = GetTypeData<T>();
        if (cache.Resources.TryGetValue(path, out var cached))
        {
            resource = (T) cached;
            return true;
        }

        if (cache.NonExistent.Contains(path))
        {
            resource = null;
            return false;
        }

        var _resource = new T();
        try
        {
            var dependencies = IoCManager.Instance!;
            _resource.Load(dependencies, path);
            resource = _resource;
            cache.Resources[path] = resource;
            return true;
        }
        catch (FileNotFoundException)
        {
            cache.NonExistent.Add(path);
            resource = null;
            return false;
        }
        catch (Exception e)
        {
            Sawmill.Error($"Exception while loading resource {typeof(T)} at '{path}'\n{e}");
            resource = null;
            return false;
        }
    }

    public bool TryGetResource(AudioStream stream, [NotNullWhen(true)] out AudioResource? resource)
    {
        resource = new AudioResource(stream);
        return true;
    }

    public void ReloadResource<T>(string path) where T : BaseResource, new()
    {
        ReloadResource<T>(new ResPath(path));
    }

    public void ReloadResource<T>(ResPath path) where T : BaseResource, new()
    {
        var cache = GetTypeData<T>();

        if (!cache.Resources.TryGetValue(path, out var res))
        {
            return;
        }

        try
        {
            var dependencies = IoCManager.Instance!;
            res.Reload(dependencies, path);
        }
        catch (Exception e)
        {
            Sawmill.Error($"Exception while reloading resource {typeof(T)} at '{path}'\n{e}");
            throw;
        }
    }

    public bool HasResource<T>(string path) where T : BaseResource, new()
    {
        return HasResource<T>(new ResPath(path));
    }

    /// <summary>
    ///     Checks whether a resource of type <typeparamref name="T"/> exists at <paramref name="path"/>.
    /// </summary>
    /// <remarks>
    ///     Delegates to <c>TryGetResource</c>, so a successful probe loads and caches the resource and a
    ///     missing file is remembered. No fallback is used.
    /// </remarks>
    public bool HasResource<T>(ResPath path) where T : BaseResource, new()
    {
        return TryGetResource<T>(path, out _);
    }

    public void CacheResource<T>(string path, T resource) where T : BaseResource, new()
    {
        CacheResource(new ResPath(path), resource);
    }

    /// <summary>
    ///     Inserts an already constructed resource into the cache under <paramref name="path"/>.
    ///     An existing entry is replaced without being disposed.
    /// </summary>
    public void CacheResource<T>(ResPath path, T resource) where T : BaseResource, new()
    {
        GetTypeData<T>().Resources[path] = resource;
    }

    /// <summary>
    ///     Returns the shared fallback resource for <typeparamref name="T"/>, loading it on first request.
    /// </summary>
    /// <exception cref="InvalidOperationException">The resource type defines no fallback.</exception>
    public T GetFallback<T>() where T : BaseResource, new()
    {
        if (_fallbacks.TryGetValue(typeof(T), out var known))
        {
            return (T) known;
        }

        // A temporary instance is only needed to read the type's fallback path.
        var fallbackPath = new T().Fallback;
        if (fallbackPath == null)
        {
            throw new InvalidOperationException($"Resource of type '{typeof(T)}' has no fallback.");
        }

        var fallback = GetResource<T>(fallbackPath.Value, useFallback: false);
        _fallbacks[typeof(T)] = fallback;
        return fallback;
    }

    public IEnumerable<KeyValuePair<ResPath, T>> GetAllResources<T>() where T : BaseResource, new()
    {
        return GetTypeData<T>().Resources.Select(p => new KeyValuePair<ResPath, T>(p.Key, (T) p.Value));
    }

    public event Action<TextureLoadedEventArgs>? OnRawTextureLoaded;
    public event Action<RsiLoadedEventArgs>? OnRsiLoaded;

    #region IDisposable Members

    private bool disposed = false;

        /// <summary>
        ///     Disposes all cached resources and suppresses finalization.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        ///     Core disposal routine. Repeated calls are no-ops once disposal has completed.
        /// </summary>
        /// <param name="disposing">
        ///     True when called from <see cref="Dispose()"/>, in which case every cached resource is disposed;
        ///     false when called from the finalizer, where managed resources must not be touched.
        /// </param>
        private void Dispose(bool disposing)
        {
            if (disposed)
            {
                return;
            }

            if (disposing)
            {
                foreach (var typeData in _cachedResources.Values)
                {
                    foreach (var resource in typeData.Resources.Values)
                    {
                        resource.Dispose();
                    }
                }
            }

            disposed = true;
        }

        /// <summary>
        ///     Finalizer: marks the cache as disposed without releasing managed resources.
        /// </summary>
        ~ResourceCache()
        {
            Dispose(false);
        }

    #endregion IDisposable Members

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private TypeData GetTypeData<T>()
    {
        return _cachedResources.GetOrNew(typeof(T));
    }

    public void TextureLoaded(TextureLoadedEventArgs eventArgs)
    {
        OnRawTextureLoaded?.Invoke(eventArgs);
    }

    public void RsiLoaded(RsiLoadedEventArgs eventArgs)
    {
        OnRsiLoaded?.Invoke(eventArgs);
    }

    private sealed class TypeData
    {
        public readonly Dictionary<ResPath, BaseResource> Resources = new();

        // List of resources which DON'T exist.
        // Needed to avoid innocuous TryGet calls repeatedly trying to re-load non-existent resources from disk.
        public readonly HashSet<ResPath> NonExistent = new();
    }
}
