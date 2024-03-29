# ScriptRenamer
Renamer Plugin for Shoko

## Installation
1. Download the [latest release](https://github.com/Mik1ll/ScriptRenamer/releases/latest)
1. Unzip the binaries in the install location/Shoko Server/plugins or in the application data folder C:\ProgramData\ShokoServer\plugins (Windows), /home/.shoko/Shoko.CLI/plugins (Linux CLI)
1. Follow instructions in the next section to add your script

## Usage
### Shoko Desktop
1. Navigate to Utilities/File Renaming
1. Use the Default script and set the type of the script to ScriptRenamer in the drop-down menu. Don't add a new script, as they are currently ignored when importing/scanning.
1. Type your script and Save (next to the script type drop-down). Note: all language keywords and labels are case insensitive.
1. Test your script using the preview utility in the same window.

### Important Notes for File Moving
If 'findLastLocation' is used, the last added file's location from the same anime will be used if it exists.
The only destination folders settable by the renamer are import folders with Drop Type of Destination or Both.  
The final destination MUST match the name or absolute path of a drop folder in order to move the file.  
If using name to set, destination import folder name must be unique or moving file will fail.  
Destination defaults to the first destination folder it can find.  
Subfolder defaults to your preferred language anime title.

## Sample Script
```
if (GroupShort)
    add '[' GroupShort '] ';
else if (GroupLong)
    add '[' GroupLong '] ';
if (AnimeTitleEnglish)
    add AnimeTitleEnglish ' ';
else
    add AnimeTitle ' ';
// Only adds episode numbers and titles if it is an episode or movie with parts
if (not (AnimeType is Movie and EpisodeTitleEnglish contains 'Complete Movie')) {
    add EpisodeNumbers pad 10;
    if (Version > 1)
        add 'v' Version;
    add ' ';
    // Don't bother with episode names if there are multiple file relations or if it doesn't have a name (these start with Episode xx)
    if (not MultiLinked and EpisodeTitleEnglish and not EpisodeTitleEnglish contains 'Episode') {
        // Episode names can get LONG, so truncate them
        add trunc(EpisodeTitleEnglish, 35);
        if (len(EpisodeTitleEnglish) > 35)
            add '...';
        add ' ';
    }
}
add '(' Resolution ' ' VideoCodecShort ' ';
if (BitDepth and BitDepth != 8)
    add BitDepth 'bit';
if (Source)
  add ' ' Source;
add ') ';
if (DubLanguages has English)
    if (DubLanguages has Japanese)
        add '[DUAL-AUDIO] ';
    else
        add '[DUB] ';
else if (DubLanguages has Japanese and not SubLanguages has English)
    add '[RAW] ';
if (Restricted)
    if (Censored)
        add '[CEN] ';
    else
        add '[UNC] ';
add '[' CRCUpper ']';
// Truncate filename just in case, old windows max path length is 260 chars
filename set trunc(Filename, 120);

if (SeriesInGroup > 1)
  subfolder set GroupName '/' AnimeTitle;
else
  subfolder set AnimeTitle;
```

### Snippets
```
if (not Sublanguages) filename add '[RAW]';
else {
    add 'subs:' Sublanguages;
}
```  
* Collections evaluate to true if it has any elements, false if it is empty.  
* If/else statements can substitute a block {} with a single statement.  
* Can optionally add 'filename' target in front of actions, it is the default target.  
* 'add' and 'set' actions take one or more strings as arguments.  
* Collections can also evaluate as a comma-separated string.

```
if (AnimeTitles has English and Main)
    subfolder set first(AnimeTitles has English and Main);
```  
* Title collections can have two filters: language and type.  
* first(***collection***) returns the first element in a collection

```
add EpisodePrefix EpisodeNumber pad MaxEpisodeCount;
if (LastEpisodeNumber != EpisodeNumber)
    add '-' LastEpisodeNumber pad MaxEpisodeCount;
```
* Episode number padding. Can use EpisodeCount or any other number, pads to match number of digits of the number on right side.
* Adds support for files with a range of episodes.
* Recommend using EpisodesNumbers instead unless you have some requirement e.g. plex episode number recognition 

```
filename set trunc(Filename, 120);
```
* Ensures that filename length do not exceed a number, useful for Windows installations that still have the 260 char limit (entire path, not just file name).
* You should truncate long strings first such as episode names before resorting to this.

```
// this is a line comment
/* this is a multi- 
line comment */
```

### Labels
#### Strings
```
AnimeTitlePreferred or AnimeTitle
AnimeTitleRomaji        // Note: these may fall back to synonym titles, use first(AnimeTitles has <Language> and <TitleType>) if you want a specific type
AnimeTitleEnglish       //
AnimeTitleJapanese      //
EpisodeTitleRomaji      // Same as above, use EpisodeTitles collection for specific type
EpisodeTitleEnglish     //
EpisodeTitleJapanese    //
GroupShort    // Release Group short name
GroupLong    // Release Group long name
CRCLower
CRCUpper
Source
Resolution    // Standardized Resolution, use Height 'x' Width for exact dimensions
AnimeType
EpisodeType
EpisodePrefix
VideoCodecLong    // Entire CodecID returned by MediaInfo (or AniDB if no local media info), usually you want the short codec
VideoCodecShort    // Simplified video codec
Duration
GroupName    // Shoko's Group name
OldFilename     // Filename before the renamer script was run
OriginalFilename    // Filename stored by AniDB when a file is added to the database
OldImportFolder    // Import folder before move **Not available while renaming**
EpisodeNumbers    // All episode numbers, as a space-seperated string. e.g. "1-3 5-6 C2 S1-2 S4 P5" Can also use padding like numbers.
Filename    // Access currently building filename
Destination // Access currently building destination
Subfolder   // Access currently building subfolder
Dates:
    AnimeReleaseDate
    EpisodeReleaseDate
    FileReleaseDate
```

#### Numbers
```
AnimeID
EpisodeID
EpisodeNumber
Version
Width
Height
EpisodeCount     // Number of episodes of this episodes type
BitDepth
AudioChannels
SeriesInGroup     // Number of series associated with a Shoko group
LastEpisodeNumber  // Same as EpisodeNumber unless file is associated with multiple episodes. Last episode in first contiguous series of episode numbers of the same EpisodeType
MaxEpisodeCount    // Max of all episode type counts
```

#### Booleans
```
Restricted
Censored
Chaptered
ManuallyLinked
InDropSource    // True if import folder moving from is a drop source
MultiLinked    // If file is linked with multiple episodes
```

#### Collections
```
AudioCodecs
DubLanguages
SubLanguages
AnimeTitles
EpisodeTitles
ImportFolders    // Available drop folders (marked as destination)
```

#### Enumerations
##### TitleType
```
Main
None
Official
Short
Synonym
```
##### EpisodeType
```
Episode
Credits
Special
Trailer
Parody
Other
```
##### AnimeType
```
Movie
OVA
TVSeries
TVSpecial
Web
Other
```
##### Language (see grammar for full list)
```
Unknown
English
Romaji
Japanese
(cont ...)
```

### Script Grammar
Refer to [this grammar](ScriptRenamer/ScriptRenamer.g4) for full syntax in [EBNF form](https://en.wikipedia.org/wiki/Extended_Backus%E2%80%93Naur_form).  

Targets:
1. filename
1. destination
1. subfolder
```
can use single wildcard in place of subfolder names to match old subfolder names at same depth.
            e.g. old: anime/mystuff/name, new: movies/*/newname, result: movies/mystuff/newname
```

Control: 
1. if (***bool expr***) ***statement***
1. if (***bool expr***) ***statement*** else ***statement***
1. { ***statement*** }    ```Standard code block, enclosing multiple statements, required after if/else statements if using multiple statements```

Statements (All end with a semicolon):
1. ***target***? add ***string***+ ;   ```Append strings to the end of the current target```
1. ***target***? set ***string***+  ;  ```Reset the target to the strings```
1. ***target***? replace ***string*** ***string*** ;    ```Replace all instances of first string by the second string in the target```
1. cancel ***string**** ;    ```Cancel renaming and moving with an exception```
1. (skipRename | skipMove) ;   ```Skip renaming or moving, deferring to the next renamer/mover in the priority list```
1. findLastLocation ;    ```Enables using last added file's location from the same anime```
1. removeReservedChars ;    ```Remove reserved characters instead of replacing them with alternatives```
1. (log | logError) ***string***+ ;    ```Logging```

Collections:
1. ***collection label***
1. ***collection label*** has ***collection enum***    ```String for AudioCodec and Import Folders (name or absolute path)```
1. ***collection label*** has ***collection enum*** and ***other collection enum***    ```Only supported by AnimeTitle and EpisodeTitle, for Types+Language enums```
1. first(***collection***)    ```Get first element of a collection```

Boolean Expressions (In order of precedence):
1. not ***bool expr***    ```Invert the value of the expression```
1. ***collection***    ```True if collection is non-empty```
1. ***type label*** is ***type enum***    ```Used by AnimeType and EpisodeType, checks the type```
1. ***string atom*** contains ***string atom***    ```True if string contains another string as a substring```
1. ***number*** (< | > | <= | >= | == | !=) ***number***    ```Only supports integers at this time```
1. ***(bool expr, number, or string)*** (== | !=) ***(bool expr, number, or string)***    ```Checks equality/inequality```
1. ***bool expr*** and ***bool expr***    ```Boolean and expression```
1. ***bool expr*** or ***bool expr***    ```Boolean or expression```
1. (***bool expr***)    ```Expression parentheses for enforcing order of operations```
1. ***bool*** 

Bools:
1. true | false
1. ***number*** ```True if non-zero```
1. ***string*** ```True if non-empty```

Numbers:
1. \[+-]?\[0-9]+
1. ***number label***
1. len(***collection*** | ***string***)    ```Length of a collection or a string```

Strings:
1. '***char****' | "***char****"
2. ***string label***
3. ***collection*** ```Comma delimited list, null if empty```
4. ***number*** (pad ***number***)?    ```Able to pad number up to same number of digits as second number, commonly used with EpisodeCount or MaxEpisodeCount. Special case: works with EpisodeNumbers string```
5. ***date***
6. ***string*** + ***string***
7. replace(***string***, ***old string***, ***new string***)    ```Returns string with old string replaced with new string```
8. rxreplace(***string***, ***pattern string***, ***replacement string***))    ```Replaces using a Regular Expression, see ```[here](https://docs.microsoft.com/en-us/dotnet/api/system.text.regularexpressions.regex?view=net-5.0)``` and ```[here](https://docs.microsoft.com/en-us/dotnet/standard/base-types/substitutions-in-regular-expressions)``` for more information.```
9. rxmatch(***string***, ***pattern string***)    ```Matches a string with a pattern and returns the first match```
10. substr(***string***, ***index number***)    ```Returns string starting at given index```
11. substr(***string***, ***index number***, ***length number***)    ```Returns string starting at given index with given length```
12. trunc(***string***, ***length number***)    ```Returns string with characters after length sliced off```
13. trim(***string***)    ```Trims whitespace on ends of string```
14. upper(***string***)    ```Convert to all uppercase```
15. lower(***string***)    ```Convert to all lowercase```
16. capitalize(***string***)    ```Convert to title case (each word capitalized)```

Dates:
1. ***date label***
1. ***date label***.***(Year, Month, or Day)***

Comments:
1. //***char*******newline*** ```Line comment```
1. /\*(***char***\*)\*/ ```Block Comment```

# Compilation
Requires Antlr4 and Java Runtime to compile  
antlr4 quick-start: https://github.com/antlr/antlr4/blob/master/doc/getting-started.md
