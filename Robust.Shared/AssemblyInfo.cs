using System.Runtime.CompilerServices;

// The attributes below expose internal types to other assemblies that are declared friends.
// SS14 engine assemblies are friends.
// This way internal is "Content can't touch this".
[assembly: InternalsVisibleTo("Robust.Server")]
[assembly: InternalsVisibleTo("Robust.Client")]
[assembly: InternalsVisibleTo("Robust.Lite")]
[assembly: InternalsVisibleTo("Robust.UnitTesting")]
[assembly: InternalsVisibleTo("OpenToolkit.GraphicsLibraryFramework")]
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")] // Gives access to Castle(Moq)
[assembly: InternalsVisibleTo("Robust.Benchmarks")]
[assembly: InternalsVisibleTo("Robust.Client.WebView")]
[assembly: InternalsVisibleTo("Robust.Packaging")]
[assembly: InternalsVisibleTo("Robust.Shared.Tests")]
[assembly: InternalsVisibleTo("Robust.Server.IntegrationTests")]
[assembly: InternalsVisibleTo("Robust.Server.Testing")]
[assembly: InternalsVisibleTo("Robust.Shared.Testing")]
[assembly: InternalsVisibleTo("Robust.Client.IntegrationTests")]
[assembly: InternalsVisibleTo("Robust.Shared.IntegrationTests")]

#if NET5_0_OR_GREATER
[module: SkipLocalsInit]
#endif

#if DEVELOPMENT
[assembly: InternalsVisibleTo("Content.Benchmarks")]
#endif
