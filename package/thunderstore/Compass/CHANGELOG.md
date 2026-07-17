# 1.0.7
* added optional distance-aware pin text scaling
* added detailed mode with separate compass, center, and mask images; both image sets are loaded and watched for changes
* migrated configuration registration from ServerSync to Conditional Config Sync with per-setting ownership defaults and server policy support
* fixed wide custom center images being cropped when no pin text is visible
* added configurable pin icon and pin text offsets
* fixed default pin text placement in bottom anchor mode
* fixed ignored pin names not being initialized at startup; exact and wildcard filters are now case-insensitive and wildcard patterns are cached
* fixed pin icons retaining the previous custom color after the color setting is reset
* fixed overlay images being rendered behind the compass
* added safe runtime normalization for invalid pin style distance conditions
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
