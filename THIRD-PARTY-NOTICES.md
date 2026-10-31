# Third-party notices

mdhub is MIT-licensed (see LICENSE). It bundles the following third-party components, each under its own licence.
None of them is modified. The full licence texts are in the `Dictionaries` folder next to the app.

## WeCantSpell.Hunspell 7.0.1 (spell checking engine)

- Source: https://github.com/aarondandy/WeCantSpell.Hunspell (NuGet package `WeCantSpell.Hunspell`)
- Licence: tri-licensed MPL 1.1 / GPL 2.0 / LGPL 2.1, inherited from Hunspell. mdhub uses the unmodified library
  under the **Mozilla Public License 1.1** option. Its source code is available at the URL above.
- Licence text: `Dictionaries/WeCantSpell.Hunspell-LICENSE.txt`

## English (US) dictionary: `en_US.aff`, `en_US.dic`

- Hunspell en_US dictionary, version 2020.12.07, derived from SCOWL (http://wordlist.aspell.net/), as shipped in
  LibreOffice's dictionaries repository (https://git.libreoffice.org/dictionaries, folder `en`).
- Licence: the SCOWL licence (permissive, BSD-like): Copyright 2000-2018 Kevin Atkinson and others; use, copy,
  modify, distribute and sell are permitted provided the copyright notice and permission notice appear in all copies.
- Licence text: `Dictionaries/en_US-README.txt` (section "COPYRIGHT, SOURCES, and CREDITS")

## French dictionary: `fr.aff`, `fr.dic`

- Dictionnaires orthographiques français 7.7, Olivier R. and contributors (https://grammalecte.net/), as shipped in
  LibreOffice's dictionaries repository (folder `fr_FR/dictionaries`).
- Licence: **Mozilla Public License 2.0** (https://www.mozilla.org/MPL/2.0/). Source: https://grammalecte.net/
- Notice: `Dictionaries/fr-README.txt`

## Spanish (Spain) dictionary: `es_ES.aff`, `es_ES.dic`

- RLA-ES Spanish spelling dictionary 2.9 (https://github.com/sbosio/rla-es), as shipped in LibreOffice's dictionaries
  repository (folder `es`).
- Licence: tri-licensed GPL 3+ / LGPL 3+ / MPL 1.1+; mdhub uses it under the **Mozilla Public License 1.1** option.
- Licence texts: `Dictionaries/es_ES-LICENSE.md`, `Dictionaries/es_ES-MPL-1.1.txt`

## Not bundled

- German (de_DE_frami): GPL 2/3 (or the OASIS 0.1 licence, which only covers apps whose primary format is ODF),
  which is not compatible with distributing it inside an MIT app, so it is left out. German UI users get the
  English dictionary unless they pick another one.
## Material Icon Theme (file-type icons)

- Source: https://github.com/material-extensions/vscode-material-icon-theme (`icons/*.svg`)
- Licence: MIT, Copyright (c) 2025 Material Extensions
- Licence text: `Dictionaries/MaterialIconTheme-LICENSE.txt`
- Only the icons mdhub uses are bundled, as offline vector path data in `Services/FileIcons.cs` (no SVG files or fonts):
  markdown, image, svg, pdf, document, json, yaml, csharp, javascript, typescript, html, css, zip, word, table,
  powerpoint, audio, video, exe, readme, certificate, git, nodejs, docker and hex, plus the generic page outline.
  Modifications: each icon is drawn in a single colour from `Styles/FileIcons.axaml` (dark and light variants).

## NuGet packages

mdhub is built on the following NuGet packages, which ship (unmodified) in the app. Licences and copyright lines are
taken from each package's `.nuspec` and bundled licence files in the NuGet cache.

### Avalonia 12.1.4 (UI framework)

Packages: Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Skia, Avalonia.HarfBuzz, Avalonia.Win32,
Avalonia.Native, Avalonia.X11, Avalonia.FreeDesktop, Avalonia.FreeDesktop.AtSpi, Avalonia.Remote.Protocol.
Source: https://github.com/AvaloniaUI/Avalonia. Licence: **MIT**.

Copyright 2013-2026 © The AvaloniaUI Project

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
documentation files (the "Software"), to deal in the Software without restriction, including without limitation the
rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit
persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
### Markdig 1.5.0 (Markdown parser)

Source: https://github.com/xoofx/markdig. Licence: **BSD-2-Clause**.

Copyright (c) Alexandre Mutel

Redistribution and use in source and binary forms, with or without modification, are permitted provided that the
following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this list of conditions and the following
   disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following
   disclaimer in the documentation and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES,
INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY,
WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
### SkiaSharp 3.119.4 and HarfBuzzSharp 8.3.1.3 (graphics and text shaping)

Packages: SkiaSharp, HarfBuzzSharp and their NativeAssets packages (Win32, Linux, macOS, WebAssembly).
Source: https://github.com/mono/SkiaSharp. Licence: **MIT**.

Copyright (c) 2015-2016 Xamarin, Inc.
Copyright (c) 2017-2018 Microsoft Corporation.

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
documentation files (the "Software"), to deal in the Software without restriction, including without limitation the
rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit
persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
The native libraries include Skia, HarfBuzz and their dependencies under their own licences; the full notices from the native packages (identical in all of them)
are in `Dictionaries/SkiaSharp-HarfBuzzSharp-THIRD-PARTY-NOTICES.txt`.

### ANGLE (Avalonia.Angle.Windows.Natives 2.1.27548.20260419, Windows OpenGL ES)

Licence: **BSD-3-Clause** style, Copyright 2018 The ANGLE Project Authors. Full text: `Dictionaries/ANGLE-LICENSE.txt`.

### MicroCom.Runtime 0.11.6

Source: https://github.com/kekekeks/MicroCom. Licence: **MIT**. Copyright 2021 © Nikita Tsukanov

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
documentation files (the "Software"), to deal in the Software without restriction, including without limitation the
rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit
persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
### Tmds.DBus.Protocol 0.94.1 (Linux D-Bus)

Source: https://github.com/tmds/Tmds.DBus. Licence: **MIT**. Copyright (c) Tom Deseyn

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
documentation files (the "Software"), to deal in the Software without restriction, including without limitation the
rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit
persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
