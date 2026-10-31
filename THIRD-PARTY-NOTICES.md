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
