# Vector sample

Open Vector.unity and enter Play mode in a URP project. Use a Game view at least 1800 pixels wide to show all three color groups. Each row shows 16, 20, 24, 48 and 96 pixel icons.

The SVG sources are stored as .svg.txt so Unity imports them as TextAssets and the sample can bake them at runtime without changing any project's SVG importer choices.

Clapperboard and search were retrieved from the official Lucide 1.49.0 tag:
https://github.com/lucide-icons/lucide/tree/1.49.0/icons

The corresponding requested distribution URLs are:
https://unpkg.com/lucide-static@1.49.0/icons/clapperboard.svg
https://unpkg.com/lucide-static@1.49.0/icons/search.svg

Radio uses the SVG supplied with this task. Lucide-LICENSE.txt contains the Lucide ISC and inherited Feather MIT notices, including radio and search. The same notices cover the SVG strings in Tests/Editor/LucideSvg.cs.
