# RobustToolbox v2

RobustToolbox v2 is an independent fork of [RobustToolbox](https://github.com/space-wizards/RobustToolbox), the game engine that Space Station 14 is built on. It is based on RobustToolbox 275.1.0 together with the changes of the Frontier fork.

The code that the upstream project had licensed under the GPL was removed or rewritten from scratch, and RobustToolbox v2 is licensed under the MIT License. Public APIs, serialized formats and network protocols were intentionally kept unchanged.

RobustToolbox v2 is not affiliated with or endorsed by the authors of the original project.

## What is in this repository

This repository is the engine only: the code shared by client and server (`Robust.Shared`), the client (`Robust.Client`), the server (`Robust.Server`) and the engine's tests. It does not start on its own. A game project (a "content" project) provides the game and references the engine.

The upstream project's wiki (<https://docs.spacestation14.io/>) documents the engine. It follows the current upstream version, while RobustToolbox v2 is based on 275.1.0.

## Building

You need Git and the .NET SDK version named in `global.json` (10.0). The repository uses Git submodules, so clone it with them:

```
git clone --recurse-submodules https://github.com/mrl4an/RobustToolbox-v2.git
```

In an existing clone, run `git submodule update --init --recursive` instead. Then, in the repository root:

```
dotnet restore
dotnet build
dotnet test --no-build
```

## License

RobustToolbox v2 is licensed under the MIT License, see [LICENSE-MIT.TXT](LICENSE-MIT.TXT). [legal.md](legal.md) explains where the code comes from, how the old GPL code was removed, and which third-party files keep their own licenses.
