# Compass

Yet another Compass mod showing map pins in the middle top part of your screen.

## Features
* you can use files from aedenthorn's [Compass](https://www.nexusmods.com/valheim/mods/851) [compatible](https://www.nexusmods.com/valheim/mods/1897) [mods](https://www.nexusmods.com/valheim/mods/2179)
* configurable conditions based on distance to change pin scale and alpha
* an option to hide checked pins
* an option to hide shared pins
* name filter using wildcards with * and ? support
* red cross on checked pins
* configurable pin type flags
* every change of config or files update compass on the fly
* an option to hold key to see shouts, player pins and pings at any distance
* an option to hold key to see pin text (or see it at all time with other config)
* anchor position option to place compass relative to top or bottom side of the screen
* detailed mode with a separate customizable image set

## Pin style conditions config

Pin style conditions define how pin should look like depending on distance.

While distance to pin increases at first it will be smaller and smaller and then after reaching certain threshold it will be more and more transparent.

Config value for this condition consists of 4 variables in form of Vector4.

It allows you to define how scale and alpha of pin will be set with different distances.

Default value is (1, 20, 250, 550).

* X - Minimum distance to show pins.
* Y - Distance where pins will start to become smaller. Size is at maximum. Alpha is at maximum.
* Z - Distance where pins will start to become more transparent. Size is at minimum. Alpha is at maximum.
* W - Maximum distance to show pins. Size is at minimum. Alpha is at minimum.

It means if pin is between 1(X) and 20(Y) distance it will have maximum configurable size and alpha (transparency).
If pin is between 20(Y) and 250(Z) distance its size will be gradually lowered with distance and its alpha will be maximum.
If pin is between 250(Z) and 550(W) distance its size will be at the configured minimum scale and its alpha will decrease with distance.
If pin is at 550(W) distance, both its size and alpha will be at their configured minimum values.

So basically first and last variables is pin visility filter.

## Custom files

On the launch mod will create `...\BepInEx\config\shudnal.Compass` folder.

Original files will be put at config directory after first launch of the game.

On every launch, missing bundled normal or detailed image files are restored to this directory.

File names to load from config directory:
* compass.png
* compass_bottom.png (used when Anchor position = Bottom)
* center.png
* center_bottom.png (used when Anchor position = Bottom)
* mask.png
* compass_detailed.png
* compass_detailed_bottom.png
* center_detailed.png
* center_detailed_bottom.png
* mask_detailed.png
* overlay.png
* underlay.png

Both image sets are loaded at startup. Changes to any listed PNG are applied on the fly, and switching Detailed mode immediately selects the corresponding compass, center, and mask images.

## Installation (manual)
Install Conditional Config Sync, then extract Compass.dll into your BepInEx\Plugins\ folder.

## Configurating
The best way to handle configs is [Configuration Manager](https://thunderstore.io/c/valheim/p/shudnal/ConfigurationManager/).

Or [Official BepInEx Configuration Manager](https://valheim.thunderstore.io/package/Azumatt/Official_BepInEx_ConfigurationManager/).

## Donation
[Buy Me a Coffee](https://buymeacoffee.com/shudnal)

## Discord
[Join server](https://discord.gg/e3UtQB8GFK)
