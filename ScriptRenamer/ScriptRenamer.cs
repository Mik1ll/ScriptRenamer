﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using Shoko.Plugin.Abstractions;
using Shoko.Plugin.Abstractions.Attributes;
using Shoko.Plugin.Abstractions.DataModels;

namespace ScriptRenamer
{
    [Renamer(RenamerId)]
    public class ScriptRenamer : IRenamer
    {
        public const string RenamerId = nameof(ScriptRenamer);
        private static string _script = string.Empty;
        private static ParserRuleContext _context;
        private static Exception _contextException;
        private static readonly Type Repofact = GetTypeFromAssemblies("Shoko.Server.Repositories.RepoFactory");
        private static readonly dynamic VideoLocalRepo = Repofact.GetProperty("VideoLocal")?.GetValue(null);
        private static readonly dynamic ImportFolderRepo = Repofact.GetProperty("ImportFolder")?.GetValue(null);

        public (IImportFolder destination, string subfolder) GetDestination(MoveEventArgs args)
        {
            var visitor = new ScriptRenamerVisitor(args);
            if (CheckBadArgs(visitor))
            {
                args.Cancel = true;
                return (null, null);
            }
            SetupAndLaunch(visitor);
            if (visitor.FindLastLocation)
            {
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
            }
            var (destfolder, olddestfolder) = GetNewAndOldDestinations(args, visitor);
            var subfolder = GetNewSubfolder(args, visitor, olddestfolder);
            return (destfolder, subfolder);
        }

        public string GetFilename(RenameEventArgs args)
        {
            var visitor = new ScriptRenamerVisitor(args);
            if (CheckBadArgs(visitor))
            {
                args.Cancel = true;
                return null;
            }
            SetupAndLaunch(visitor);
            return !string.IsNullOrWhiteSpace(visitor.Filename)
                ? RemoveInvalidFilenameChars(visitor.Filename.ReplaceInvalidPathCharacters()) + Path.GetExtension(args.FileInfo.Filename)
                : null;
        }

        private static Type GetTypeFromAssemblies(string typeName)
        {
            return AppDomain.CurrentDomain.GetAssemblies().Select(currentassembly => currentassembly.GetType(typeName, false, true))
                .FirstOrDefault(t => t is not null);
        }

        private static string GetNewSubfolder(MoveEventArgs args, ScriptRenamerVisitor visitor, IImportFolder olddestfolder)
        {
            if (visitor.Destination is not null && visitor.Subfolder is null)
                throw new ArgumentException("Destination set without Subfolder");
            var oldsubfolder = olddestfolder is null
                ? null
                : $"{NormPath(Path.GetDirectoryName(args.FileInfo.FilePath))}/"
                    .Replace($"{NormPath(olddestfolder.Location)}/", null, StringComparison.OrdinalIgnoreCase).TrimEnd('/');
            var oldsubfoldersplit = olddestfolder is null ? Array.Empty<string>() : oldsubfolder.Split('/');
            var newsubfoldersplit = visitor.Subfolder.Trim((char)0x1F).Split((char)0x1F)
                .Select(f => f == "*" ? f : RemoveInvalidFilenameChars(f.ReplaceInvalidPathCharacters())).ToArray();
            var subfolder = string.Empty;
            for (var i = 0; i < newsubfoldersplit.Length; i++)
                if (newsubfoldersplit[i] == "*")
                    if (i < oldsubfoldersplit.Length)
                        subfolder += oldsubfoldersplit[i] + '/';
                    else
                        throw new ArgumentException("Could not find subfolder from wildcard");
                else
                    subfolder += newsubfoldersplit[i] + '/';
            subfolder = NormPath(subfolder);
            return subfolder == string.Empty ? throw new ArgumentException("Subfolder cannot be set to empty") : subfolder;
        }

        private static (IImportFolder destfolder, IImportFolder olddestfolder) GetNewAndOldDestinations(MoveEventArgs args, ScriptRenamerVisitor visitor)
        {
            if (visitor.Destination is null && visitor.Subfolder is not null)
                throw new ArgumentException("Subfolder set without Destination");
            var olddestfolder = args.AvailableFolders.OrderByDescending(f => f.Location.Length)
                .FirstOrDefault(f => f.DropFolderType.HasFlag(DropFolderType.Destination)
                                     && $"{NormPath(args.FileInfo.FilePath)}/".StartsWith($"{NormPath(f.Location)}/", StringComparison.OrdinalIgnoreCase));
            var destfolder = args.AvailableFolders.SingleOrDefault(f =>
                (string.Equals(f.Name, visitor.Destination, StringComparison.OrdinalIgnoreCase)
                 || string.Equals(NormPath(f.Location), NormPath(visitor.Destination), StringComparison.OrdinalIgnoreCase)
                ) && f.DropFolderType.HasFlag(DropFolderType.Destination));
            if (destfolder is null && visitor.Destination is not null)
                throw new ArgumentException($"Bad destination: {visitor.Destination}");
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
            ScriptRenamerLexer lexer = new(inputStream);
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

        private static bool CheckBadArgs(ScriptRenamerVisitor visitor)
        {
            if (string.IsNullOrWhiteSpace(visitor.Script?.Script))
                throw new ArgumentException("Script is empty or null");
            if (visitor.Script.Type != RenamerId)
                throw new ArgumentException($"Script doesn't match {RenamerId}");
            return visitor.AnimeInfo is null || visitor.EpisodeInfo is null;
        }

        public static string NormPath(string path)
        {
            return path?.Replace('\\', '/').TrimEnd('/');
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
