# Legal Info

RobustToolbox v2 is an independent fork of RobustToolbox. It is not affiliated with or endorsed by the authors of the original project.

## Summary

* **The old GPL code has been removed.** The code that the upstream RobustToolbox project had licensed under the GPL version 3 was deleted or rewritten from scratch for this repository, and the GPL license text is no longer shipped with it. See "Origin and removal of the GPL code" below.
* **RobustToolbox v2 is licensed under the MIT License** (see `LICENSE-MIT.TXT`). This covers all source code and all other files of the repository, except the third-party material listed under "Third-party material", which keeps its own license.
* All images in the repository are released under CC0 1.0 (see "Assets").

## Copyright

The copyright notices are in `LICENSE-MIT.TXT`. Each contributor keeps the copyright to the work they submitted to this repository.

## Source code license

All source code in this repository is licensed under the MIT License (`LICENSE-MIT.TXT`). The same applies to the other files that are not listed under "Third-party material", such as shaders, prototypes, localization files, scripts and documentation.

The following contributors have agreed to relicense their earlier contributions to MIT:

* PJB3005

### Origin and removal of the GPL code

RobustToolbox v2 is a fork of RobustToolbox (<https://github.com/space-wizards/RobustToolbox>) at version 275.1.0, together with the changes of the Frontier fork. Upstream licensed the source code contributed **before** 13 March, 2019 at 12:00:00 UTC under the GPL version 3, and the code contributed **after** that moment under the MIT license.

All code that was still attributed to GPL-era contributors other than PJB3005 (determined with `git blame` on the original project history) was removed or rewritten from scratch for this repository, because those authors could not be asked for consent:

* Unused code and tests that contained such code were deleted.
* The remaining code was rewritten with a clean-room process: a behavioural specification was written from the old code (signatures and observable behaviour, no code or comments), and the new code was then implemented only from that specification by someone who had not seen the old code. All documentation comments are new. The maintainers keep a log of this process, including every incident in which its rules were broken.
* Public APIs, serialized formats and network protocols were intentionally kept unchanged so that the engine stays compatible. Declarations, attribute lists, wire-protocol constants and similar lines that are dictated by these interfaces are therefore the same as in upstream.

The GPL license text is no longer shipped, and the git history of the original project is not part of this repository because it contains the removed code. The repository starts with a single commit.

### Frontier fork

This fork is based on the Frontier fork of RobustToolbox, which adds about 20 commits by its maintainer (GitHub: HacksLua) on top of upstream version 275.1.0. Those commits were contributed after 13 March, 2019, to a repository whose legal notice states that such contributions are licensed under the MIT license, and no other terms were stated, so they are treated as MIT-licensed.

## Third-party material

Some files are not original to this project. They keep the license named in their own notices, which stay in the files or next to them:

* Physics code derived from Box2D (Erin Catto), Farseer Physics Engine (Ian Qvist) and Aether.Physics2D (Kastellanos Nikolaos) in `Robust.Shared/Physics/` and in `Robust.Shared.Maths/Matrix22.cs` and `Matrix33.cs`. The zlib, MIT or Microsoft Permissive License v1.1 terms stated in the header of each file apply to that file.
* Parts of `Robust.Shared.Maths/Color.cs`, `Robust.Shared.Maths/MathHelper.cs` and `Robust.Shared/Audio/Effects/`, which come from the OpenTK library (MIT/X11 license).
* `Robust.Shared/Noise/FastNoise.cs` and `FastNoiseLite.cs` (Jordan Peck, MIT), `Robust.Shared/Utility/PriorityQueue.cs` (Denis Shulepov, MIT), `Robust.Shared/Collections/ValueList.cs` (based on the .NET `List<T>` class, .NET Foundation, MIT), `Resources/Shaders/color_picker.swsl` (Inigo Quilez, MIT) and `Tools/exe_set_subsystem.py` (20kdc, MIT).
* The DocFX template in `Robust.Docfx/templates/darkfx/` (MIT, see the license file in that folder).
* The Git submodules `Lidgren.Network` (MIT), `NetSerializer` (MPL 2.0), `XamlX` (MIT), `cefglue` (MIT) and `Robust.LoaderApi` (MIT). They are separate projects with their own license files.
* The libraries that the engine uses are credited, together with their license texts, in `Resources/EngineCredits/Libraries.yml`. Two of them, FluidSynth and OpenAL Soft, are LGPL-licensed native libraries; they are not part of this repository.

## Assets

All images in this repository (the placeholder textures below) were drawn by mrl4an and are released under CC0 1.0 Universal (public domain dedication, <https://creativecommons.org/publicdomain/zero/1.0/>):

* `Resources/Textures/noSprite.png`
* `Resources/Textures/noTile.png`
* `Resources/Textures/error.rsi/`
* `Resources/Textures/debugRotation.rsi/`
* `Resources/Textures/Interface/Default/cross.png`
* `Resources/Textures/Interface/tilebuildoverlay.png`

The fallback soundfont `Resources/Midi/fallback.sf2` is a third-party file published with no rights reserved (source: <https://musical-artifacts.com/artifacts/9429>, see `Resources/Midi/copyright.txt`).

The fonts in `Resources/EngineFonts/NotoSans/` are third-party and keep their own licenses:

* `NotoSans-Regular.ttf` (Copyright 2012 Google Inc.) is licensed under the Apache License 2.0; the license text is `LICENSE.txt` in that folder.
* `NotoSansMono-Regular.ttf` (Copyright 2022 The Noto Project Authors) is licensed under the SIL Open Font License 1.1; the license text is `NotoSansMono-OFL.txt` in that folder.

## Warranty

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
