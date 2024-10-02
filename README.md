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

## Pin style conditions config

Pin style conditions define how pin should look like depending on distance.

While distance to pin increases at first it will be smaller and smaller and then after reaching certain threshold it will be more and more transparent.

Config value for this condition consists of 4 variables in form of Vector4.

It allows you to define how scale and alpha of pin will be set with different distances.

Default value is (1, 20, 250, 350).

* X - Minimum distance to show pins.
* Y - Distance where pins will start to become smaller. Size is at maximum. Alpha is at maximum.
* Z - Distance where pins will start to become more transparent. Size is at minimum. Alpha is at maximum.
* W - Maximum distance to show pins. Size is at minimum. Alpha is at minimum.

It means if pin is between 1(X) and 20(Y) distance it will have maximum configurable size and alpha (transparency).
If pin is between 20(Y) and 250(Z) distance its size will be gradually lowered with distance and its alpha will be maximum.
If pin is between 250(Z) and 350(W) distance its size will be as it set in minimum scale value and now it's alpha will be decreased with distance.
If pin is at 350(W) distance its size will be minimum and so as its alpha.

So basically first and last variables is pin visility filter.

## Custom files

On the launch mod will create `...\BepInEx\config\shudnal.Compass` folder.

Original files will be put at config directory after first launch of the game.

On every launch if any of files `compass.png, center.png, mask.png` is missed it will be put there as this files are mandatory.

File names to load from config directory:
* compass.png
* center.png
* mask.png
* overlay.png
* underlay.png

Files will be loaded on the fly after change.

## Installation (manual)
extract Compass.dll into your BepInEx\Plugins\ folder

## Configurating
The best way to handle configs is [Configuration Manager](https://thunderstore.io/c/valheim/p/shudnal/ConfigurationManager/).

Or [Official BepInEx Configuration Manager](https://valheim.thunderstore.io/package/Azumatt/Official_BepInEx_ConfigurationManager/).