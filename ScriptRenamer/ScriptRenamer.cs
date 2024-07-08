using System;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using Antlr4.Runtime;
using Microsoft.Extensions.Logging;
using Shoko.Plugin.Abstractions;
using Shoko.Plugin.Abstractions.Attributes;
using Shoko.Plugin.Abstractions.DataModels;
using Shoko.Plugin.Abstractions.Enums;


namespace ScriptRenamer
{
    [RenamerID(nameof(ScriptRenamer))]
    public class ScriptRenamer : IRenamer<ScriptRenamerSettings>
    {
        private readonly ILogger<ScriptRenamer> _logger;
        private static string _script = string.Empty;
        private static ParserRuleContext _context;
        private static Exception _contextException;

        public string Name { get; } = nameof(ScriptRenamer);
        public string Description { get; } = "Made by Mikill(Discord)/Mik1ll(Github)";
        public bool SupportsMoving { get; } = true;
        public bool SupportsRenaming { get; } = true;

        public ScriptRenamerSettings DefaultSettings { get; } = new()
        {
            Script = """
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
                     """
        };

        public ScriptRenamer(ILogger<ScriptRenamer> logger)
        {
            _logger = logger;
        }

        private static (IImportFolder destination, string subfolder)? FindLastFileLocation(ScriptRenamerVisitor visitor)
        {
            var availableLocations = visitor.AnimeInfo.VideoList
                .Where(vl => !string.Equals(vl.Hashes.ED2K, visitor.VideoInfo.Hashes.ED2K, StringComparison.OrdinalIgnoreCase))
                .SelectMany(vl => vl.Locations.Select(l => new
                {
                    l.ImportFolder,
                    SubFolder = SubfolderFromRelativePath(l)
                }))
                .Where(vlp => !string.IsNullOrWhiteSpace(vlp.SubFolder) && vlp.ImportFolder is not null &&
                              (vlp.ImportFolder.DropFolderType.HasFlag(DropFolderType.Destination) ||
                               vlp.ImportFolder.DropFolderType.HasFlag(DropFolderType.Excluded)));
            var bestLocation = availableLocations.GroupBy(l => l.SubFolder)
                .OrderByDescending(g => g.ToList().Count).Select(g => g.First())
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(bestLocation?.SubFolder) || bestLocation.ImportFolder is null) return null;
            return (bestLocation.ImportFolder, bestLocation.SubFolder);
        }


        private static string SubfolderFromRelativePath(IVideoFile videoFile)
        {
            return Path.GetDirectoryName(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }.Contains(videoFile.RelativePath[0])
                ? videoFile.RelativePath[1..]
                : videoFile.RelativePath);
        }


        private static string GetNewSubfolder(RelocationEventArgs args, ScriptRenamerVisitor visitor, IImportFolder olddestfolder)
        {
            if (visitor.Subfolder is null)
                return RemoveInvalidFilenameChars(args.AnimeInfo.OrderBy(a => a.ID).First().PreferredTitle is var title && visitor.RemoveReservedChars
                    ? title
                    : title.ReplaceInvalidPathCharacters());
            var oldsubfolder = string.Empty;
            if (olddestfolder is not null)
            {
                var olddest = NormPath(olddestfolder.Path) + Path.DirectorySeparatorChar;
                oldsubfolder = Path.GetRelativePath(olddest, Path.GetDirectoryName(NormPath(args.FileInfo.Path))!);
            }

            var subfolder = string.Empty;
            var oldsubfoldersplit = olddestfolder is null ? Array.Empty<string>() : oldsubfolder.Split(Path.DirectorySeparatorChar);
            var newsubfoldersplit = visitor.Subfolder.Trim((char)0x1F).Split((char)0x1F)
                .Select(f => f == "*" ? f : RemoveInvalidFilenameChars(visitor.RemoveReservedChars ? f : f.ReplaceInvalidPathCharacters())).ToArray();
            for (var i = 0; i < newsubfoldersplit.Length; i++)
                if (newsubfoldersplit[i] == "*")
                    if (i < oldsubfoldersplit.Length)
                        subfolder += oldsubfoldersplit[i] + Path.DirectorySeparatorChar;
                    else
                        throw new ScriptRenamerException("Could not find subfolder from wildcard");
                else
                    subfolder += newsubfoldersplit[i] + Path.DirectorySeparatorChar;
            subfolder = NormPath(subfolder);
            return subfolder;
        }

        private static (IImportFolder destfolder, IImportFolder olddestfolder) GetNewAndOldDestinations(RelocationEventArgs args, ScriptRenamerVisitor visitor)
        {
            IImportFolder destfolder;
            if (string.IsNullOrWhiteSpace(visitor.Destination))
            {
                destfolder = args.AvailableFolders
                    // Order by common prefix (stronger version of same drive)
                    .OrderByDescending(f => string.Concat(NormPath(args.FileInfo.Path)
                        .TakeWhile((ch, i) => i < NormPath(f.Path).Length
                                              && char.ToUpperInvariant(NormPath(f.Path)[i]) == char.ToUpperInvariant(ch))).Length)
                    .FirstOrDefault(f => f.DropFolderType.HasFlag(DropFolderType.Destination));
            }
            else
            {
                destfolder = args.AvailableFolders.FirstOrDefault(f =>
                    f.DropFolderType.HasFlag(DropFolderType.Destination)
                    && (string.Equals(f.Name, visitor.Destination, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(NormPath(f.Path), NormPath(visitor.Destination), StringComparison.OrdinalIgnoreCase))
                );
                if (destfolder is null)
                    throw new ScriptRenamerException($"Bad destination: {visitor.Destination}");
            }

            var olddestfolder = args.AvailableFolders.OrderByDescending(f => f.Path.Length)
                .FirstOrDefault(f => f.DropFolderType.HasFlag(DropFolderType.Destination)
                                     && NormPath(args.FileInfo.Path).StartsWith(NormPath(f.Path), StringComparison.OrdinalIgnoreCase));
            return (destfolder, olddestfolder);
        }

        private static void SetContext(string script)
        {
            if (script == _script)
                if (_contextException is not null)
                    ExceptionDispatchInfo.Capture(_contextException).Throw();
                else if (_context is not null)
                    return;
            _script = script;
            _contextException = null;
            _context = null;
            AntlrInputStream inputStream = new(new StringReader(script));
            CaseChangingCharStream lowerstream = new(inputStream, false);
            ScriptRenamerLexer lexer = new(lowerstream);
            lexer.AddErrorListener(ExceptionErrorListener.Instance);
            CommonTokenStream tokenStream = new(lexer);
            ScriptRenamerParser parser = new(tokenStream);
            parser.ErrorHandler = new BailErrorStrategy();
            parser.AddErrorListener(ExceptionErrorListener.Instance);
            try
            {
                _context = parser.start();
            }
            catch (Exception e)
            {
                _contextException = e;
                ExceptionDispatchInfo.Capture(e).Throw();
            }
        }

        private static void SetupAndLaunch(ScriptRenamerVisitor visitor)
        {
            SetContext(visitor.Script);
            try
            {
                _ = visitor.Visit(_context);
            }
            catch (SkipException)
            {
                visitor.Filename = null;
                visitor.Destination = null;
                visitor.Subfolder = null;
            }
        }

        private static void CheckBadArgs(ScriptRenamerVisitor visitor)
        {
            if (string.IsNullOrWhiteSpace(visitor.Script))
                throw new ScriptRenamerException("Script is empty or null");
            if (visitor.AnimeInfo == null || visitor.EpisodeInfo is null)
                throw new ScriptRenamerException("No anime and/or episode info");
        }

        public static string NormPath(string path)
        {
            return path?.Replace('/', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);
        }

        private static string RemoveInvalidFilenameChars(string filename)
        {
            filename = filename.RemoveInvalidPathCharacters();
            filename = string.Concat(filename.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
            return filename;
        }


        public RelocationResult GetNewPath(RelocationEventArgs<ScriptRenamerSettings> args)
        {
            var visitor = new ScriptRenamerVisitor(args, _logger);
            CheckBadArgs(visitor);
            SetupAndLaunch(visitor);
            IImportFolder destfolder = null;
            string subfolder = null;
            string filename = null;
            if (!visitor.SkipMove)
            {
                if (visitor.FindLastLocation)
                {
                    (destfolder, subfolder) = FindLastFileLocation(visitor) ?? (null, null);
                }

                if (destfolder is null || subfolder is null)
                {
                    (destfolder, var olddestfolder) = GetNewAndOldDestinations(args, visitor);
                    subfolder = GetNewSubfolder(args, visitor, olddestfolder);
                }
            }

            if (!visitor.SkipRename)
                filename = !string.IsNullOrWhiteSpace(visitor.Filename)
                    ? RemoveInvalidFilenameChars(visitor.RemoveReservedChars ? visitor.Filename : visitor.Filename.ReplaceInvalidPathCharacters()) +
                      Path.GetExtension(args.FileInfo.FileName)
                    : null;

            return new RelocationResult { Path = subfolder, DestinationImportFolder = destfolder, FileName = filename };
        }
    }

    public class ScriptRenamerSettings
    {
        [RenamerSetting(Type = RenamerSettingType.Code, Language = CodeLanguage.PlainText)]
        public string Script { get; set; }
    }

    public class SkipException : Exception
    {
    }
}
