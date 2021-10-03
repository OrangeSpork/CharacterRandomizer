# CharacterRandomizer

Studio Plugin that allows either manual or timing based replacement of characters in scenes. Let's you view your scene with different characters easily.

Started as a personal plugin for a 'surprise me' button to swap a scene with a new character (particularly nice with VR scenes). Then started snowballing...'well it would be nice if...' moments.

## Plugin Settings (in the F1 plugin settings menu)

**Trigger Character Replacement** : Hotkey to trigger the replacement for the selected character. Unbound by default.\
**Default subdirectory** : If not otherwise specified pull characters from the following subdirectory. Directory is based from the female or male level. Leave empty to use those directories. Pipe delimit multiple subdirectories together.\
**Minimum Replacement Delay** : The timer uses a Minimum + Base + Random formula. This sets the minimum, setting this low can result in your scene soft-locking as it continuously replaces characters, make sure you leave this long enough to load scenes before replacements start firing.

## Character Settings (Configure using the toolbar icon - the dice)

**Running** : Whether timer based replacement is currently...running...for this character.\
**Preserve Outfit** : Attempts to hang onto the current character outfit. Experimental, not entirely supported by studio, seems to work but certain plugins don't like it, noteably Material Editor but otherwise works.\
**Outfit Coordinate** : Specify a coordinate file name to be loaded after replacement. More stable than Preserve Outfit. Just the file name (ex. HS2CoordeF_20210828223242029.png ). Leave blank to not load an outfit.\
**No Duplicate Characters** : Picks characters not already in the scene. Note that the initial scene characters aren't known so the first replacement could dupe a character originally in the scene.\
**Use Sync Timers** : Sync'd characters all replace on the same timer. Unsync'd characters have their own timers. Only effects the timer based replacement and the 'Replace all Sync'd' button. Note changing timer settings on any sync'd character changes them all.\
**Replacement Mode** : Random picks new character randomly from available options. Cyclics cycle in the specified sort order. Initial replacement starts at the top or bottom, but once a character has been replaced changing sort cycles from the current character wherever they are in the order.\
**Rotation Mode** : Only affects Sync'd characters (see Use Sync Timers) - if manually triggered use Replace all Sync'd. Rotation cycles characters through the various slots in the scene. First cycle either the first or last character is replaced using the other options here. Next cycle that first/last character is rotated into the next slot position and a new first last is selected. Next cycle after that slot 2 is moved to 3, 1 to 2 and a new first/last is selected. And so on until all slots are cycling, with a new first/last character picked and the last slot rotated out.\
**Base Time**: Replacement timer uses the formula (MIN + Base + Random) seconds. Minimum is from plugin settings, this is the flat base number. Don't set it too low or you risk a continuous replacement loop.\
**Random Time**: The random component of the replacement timer adds a value from 0 to this number to the timer.\
**Included Subdirectories**: Subdirectories to search for characters in. Empty is the base female/male directory. Pipe delimit multiple selections.
- Example 1: 'Favorites|Recent' would read the UserData/chara/female/Favorites and UserData/chara/female/Recent directories
- Example 2: '|Variants*' would read the UserData/chara/female directory and the UserData/chara/female/Variants (and all children). Note * can only be used at the end to indicate children are included.

**Name Pattern Text**: Regular expression to be used in filtering characters by name. All character candidates are run through this before selection (regardless of mode). Use .* for all.

## Actions

**Refresh Char Lists** : The plugin scans available characters on startup, if you add/remove/move things around you need to refresh to get them to be able to be selected properly. Push this to do so.\
**Replace Me** : Immediately replaces the currently selected character based on the options above.\
**Replace All Sync'd** : Immediately replaces all sync'd characters (Use Sync Timers checked) based on current character options. Also available without a character being selected.

