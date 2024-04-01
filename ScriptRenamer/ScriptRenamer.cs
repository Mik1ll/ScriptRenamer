using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using Antlr4.Runtime;
using Microsoft.Extensions.Logging;
using Shoko.Plugin.Abstractions;
using Shoko.Plugin.Abstractions.Attributes;
using Shoko.Plugin.Abstractions.DataModels;


namespace ScriptRenamer
{
    [Renamer(nameof(ScriptRenamer))]
    // ReSharper disable once ClassNeverInstantiated.Global
    public class ScriptRenamer : IRenamer
    {
        private readonly ILogger<ScriptRenamer> _logger;
        private static string _script = string.Empty;
        private static ParserRuleContext _context;
        private static Exception _contextException;
        private static readonly Type Repofact = GetTypeFromAssemblies("Shoko.Server.Repositories.RepoFactory");
        private static readonly dynamic VideoLocalRepo = Repofact?.GetProperty("VideoLocal")?.GetValue(null);
        private static readonly dynamic ImportFolderRepo = Repofact?.GetProperty("ImportFolder")?.GetValue(null);

        public ScriptRenamer(ILogger<ScriptRenamer> logger)
        {
            _logger = logger;
        }
        
        public (IImportFolder destination, string subfolder) GetDestination(MoveEventArgs args)
        {
            var visitor = new ScriptRenamerVisitor(args, false, _logger);
            CheckBadArgs(visitor);
            SetupAndLaunch(visitor);
            if (visitor.FindLastLocation)
            {
                var result = FindLastFileLocation(visitor);
                if (result.HasValue)
                    return result.Value;
            }
            var (destfolder, olddestfolder) = GetNewAndOldDestinations(args, visitor);
            var subfolder = GetNewSubfolder(args, visitor, olddestfolder);
            return (destfolder, subfolder);
        }

        private static (IImportFolder destination, string subfolder)? FindLastFileLocation(ScriptRenamerVisitor visitor)
        {
            if (VideoLocalRepo is null || ImportFolderRepo is null)
                return null;
            IImportFolder fld = null;
            var lastFileLocation = ((IEnumerable<dynamic>)VideoLocalRepo.GetByAniDBAnimeID(visitor.AnimeInfo.AnimeID))
                .Where(vl => !string.Equals(vl.CRC32, visitor.FileInfo.Hashes.CRC, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(vl => vl.DateTimeUpdated)
                .Select(vl => vl.GetBestVideoLocalPlace())
                .FirstOrDefault(vlp => (fld = (IImportFolder)ImportFolderRepo.GetByID(vlp.ImportFolderID)) is not null &&
                                       (fld.DropFolderType.HasFlag(DropFolderType.Destination) || fld.DropFolderType.HasFlag(DropFolderType.Excluded)));
            string subFld = Path.GetDirectoryName(lastFileLocation?.FilePath);
            if (fld is not null && subFld is not null)
                return (fld, subFld);
            return null;
        }

        public string GetFilename(RenameEventArgs args)
        {
            var visitor = new ScriptRenamerVisitor(RenameArgsToMoveArgs(args), true, _logger)
            {
                Renaming = true
            };
            CheckBadArgs(visitor);
            SetupAndLaunch(visitor);
            return !string.IsNullOrWhiteSpace(visitor.Filename)
                ? RemoveInvalidFilenameChars(visitor.RemoveReservedChars ? visitor.Filename : visitor.Filename.ReplaceInvalidPathCharacters()) +
                  Path.GetExtension(args.FileInfo.Filename)
                : null;
        }

        private static MoveEventArgs RenameArgsToMoveArgs(RenameEventArgs args) =>
        new(
            args.Script,
            ImportFolderRepo is not null ? ((IEnumerable)ImportFolderRepo.GetAll()).Cast<IImportFolder>().Where(a => a.DropFolderType != DropFolderType.Excluded).ToList<IImportFolder>() : new List<IImportFolder>(),
            args.FileInfo,
            args.VideoInfo,
            args.EpisodeInfo,
            args.AnimeInfo,
            args.GroupInfo
        )
        {
            Cancel = args.Cancel,
        };

        private static Type GetTypeFromAssemblies(string typeName)
        {
            return AppDomain.CurrentDomain.GetAssemblies().Select(currentassembly => currentassembly.GetType(typeName, false, true))
                .FirstOrDefault(t => t is not null);
        }

        private static string GetNewSubfolder(MoveEventArgs args, ScriptRenamerVisitor visitor, IImportFolder olddestfolder)
        {
            if (visitor.Subfolder is null)
                return RemoveInvalidFilenameChars(args.AnimeInfo.OrderBy(a => a.AnimeID).First().PreferredTitle is var title && visitor.RemoveReservedChars
                    ? title
                    : title.ReplaceInvalidPathCharacters());
            var oldsubfolder = string.Empty;
            if (olddestfolder is not null)
            {
                var olddest = NormPath(olddestfolder.Location) + Path.DirectorySeparatorChar;
                oldsubfolder = Path.GetRelativePath(olddest, Path.GetDirectoryName(NormPath(args.FileInfo.FilePath))!);
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
                        throw new ArgumentException("Could not find subfolder from wildcard");
                else
                    subfolder += newsubfoldersplit[i] + Path.DirectorySeparatorChar;
            subfolder = NormPath(subfolder);
            return subfolder;
        }

        private static (IImportFolder destfolder, IImportFolder olddestfolder) GetNewAndOldDestinations(MoveEventArgs args, ScriptRenamerVisitor visitor)
        {
            IImportFolder destfolder;
            if (string.IsNullOrWhiteSpace(visitor.Destination))
            {
                destfolder = args.AvailableFolders
                    // Order by common prefix (stronger version of same drive)
                    .OrderByDescending(f => string.Concat(NormPath(args.FileInfo.FilePath)
                        .TakeWhile((ch, i) => i < NormPath(f.Location).Length
                                              && char.ToUpperInvariant(NormPath(f.Location)[i]) == char.ToUpperInvariant(ch))).Length)
                    .FirstOrDefault(f => f.DropFolderType.HasFlag(DropFolderType.Destination));
            }
            else
            {
                destfolder = args.AvailableFolders.FirstOrDefault(f =>
                    f.DropFolderType.HasFlag(DropFolderType.Destination)
                    && (string.Equals(f.Name, visitor.Destination, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(NormPath(f.Location), NormPath(visitor.Destination), StringComparison.OrdinalIgnoreCase))
                );
                if (destfolder is null)
                    throw new ArgumentException($"Bad destination: {visitor.Destination}");
            }

            var olddestfolder = args.AvailableFolders.OrderByDescending(f => f.Location.Length)
                .FirstOrDefault(f => f.DropFolderType.HasFlag(DropFolderType.Destination)
                                     && NormPath(args.FileInfo.FilePath).StartsWith(NormPath(f.Location), StringComparison.OrdinalIgnoreCase));
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
            SetContext(visitor.Script.Script);
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
            if (string.IsNullOrWhiteSpace(visitor.Script?.Script))
                throw new ArgumentException("Script is empty or null");
            if (visitor.Script.Type != nameof(ScriptRenamer))
                throw new ArgumentException($"Script doesn't match {nameof(ScriptRenamer)}");
            if (visitor.AnimeInfo == null || visitor.EpisodeInfo is null)
                throw new ArgumentException("No anime and/or episode info");
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
    }

    public class SkipException : Exception
    {
    }
}
