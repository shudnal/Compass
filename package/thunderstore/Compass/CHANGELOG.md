# 1.1.2
* Reused pin UI elements with a bounded inactive reserve instead of rebuilding the entire list when visible pin counts change.
* Sampled orientation, distances and display shortcuts once per refresh, and removed allocating image selection and duplicate-pin scans.
* Avoided redundant text and layout updates while preserving live pin positions, localization, UGC filtering and image reloads.
* Fixed inconsistent ordering of overlapping event-area pins and invalid text transforms at zero icon scale.

# 1.1.1
* Updated for the Valheim 1.0.7 release.
* Updated required dependencies to BepInExPack Valheim 5.4.2350 and ConditionalConfigSync 1.0.5.

# 1.1.0
* new feature: detailed mode with separate compass, center, and mask images. Thanks to BETLOG for providing images.
* fixed default pin text placement in bottom anchor mode
* added configurable pin icon and pin text offsets
* migrated configuration registration to Conditional Config Sync with per-setting ownership defaults and server policy support
* fixed wide custom center images being cropped when no pin text is visible
* added optional per-image server synchronization for all compass PNG files through Conditional Config Sync
* added optional distance-aware pin text scaling
* fixed ignored pin names not being initialized at startup; exact and wildcard filters are now case-insensitive and wildcard patterns are cached
* fixed overlay images being rendered behind the compass
* improved custom image hot reload handling for partial writes, atomic renames, and invalid PNG data

# 1.0.6
* added compass anchor position config option (top/bottom) with offset relative to selected anchor
* added `compass_bottom.png` and `center_bottom.png` files for bottom anchor mode
* pin text now flips above pin icon when using bottom anchor mode

# 1.0.5
fixed incorrect default pin text style

# 1.0.4
* configurable pin text style

# 1.0.3
* patch 0.220.3
* default texture wrap mode changed to clamp to avoid occasional last line pixel mirroring
* compass scale now respects GUI scaling from game settings

# 1.0.2
* fixed last death pin wasn't showing
* default maximum distance increased to 550
* new config option to hold key to see shouts at any distance
* new config option to hold key to see other players at any distance
* new config option to hold key to see pings at any distance
* new config option to hold key to see pin text
* shouts, players and pings always show its text if any distance key is hold

# 1.0.1
 * Fixed possible error after world relaunch

# 1.0.0
 * Initial Release
