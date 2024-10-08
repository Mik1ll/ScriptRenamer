using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Antlr4.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using ScriptRenamer;
using Shoko.Plugin.Abstractions.DataModels;
using Shoko.Plugin.Abstractions.DataModels.Shoko;
using Shoko.Plugin.Abstractions.Events;

// ReSharper disable PossibleUnintendedReferenceComparison

namespace ScriptRenamerTests
{
    [TestClass]
    public class ParserTest
    {
        private static ScriptRenamerParser Setup(string text)
        {
            AntlrInputStream inputStream = new(text);
            CaseChangingCharStream lowerStream = new(inputStream, false);
            ScriptRenamerLexer lexer = new(lowerStream);
            lexer.AddErrorListener(ExceptionErrorListener.Instance);
            CommonTokenStream tokenStream = new(lexer);
            ScriptRenamerParser parser = new(tokenStream)
            {
                ErrorHandler = new BailErrorStrategy()
            };
            parser.AddErrorListener(ExceptionErrorListener.Instance);
            return parser;
        }

        [DataTestMethod]
        [DataRow(@"if (GroupShort)
                    add '[' GroupShort '] ';
                else if (GroupLong)
                    add '[' GroupLong '] ';
                if (AnimeTitleEnglish)
                    add AnimeTitleEnglish ' ';
                else
                    add AnimeTitle ' ';
                add EpisodePrefix;
                if (EpisodeType is Episode and len(EpisodeCount) >= 2 and EpisodeNumber <= 9)
                    add '0';
                add EpisodeNumber;
                if (Version > 1)
                    add 'v' Version;
                add ' ';
                if (EpisodeTitleEnglish)
                    add EpisodeTitleEnglish ' ';
                else
                    add first(EpisodeTitles has Main) ' ';
                add Resolution ' ' VideoCodecShort ' ';
                if (BitDepth)
                    add BitDepth 'bit ';
                add Source ' ';
                if (DubLanguages has English)
                    if (DubLanguages has Japanese)
                        add '[DUAL-AUDIO] ';
                    else
                        add '[DUB] ';
                else if (DubLanguages has Japanese and not SubLanguages has English)
                    add '[raw] ';
                if (Restricted)
                    if (Censored)
                        add '[CEN] ';
                    else
                        add '[UNC] ';
                add '[' CRCUpper ']';

                // Import folders:
                if (Restricted and ImportFolders has 'h-anime')
                    destination set 'h-anime';
                else if (AnimeType is Movie)
                    destination set 'Movies';
                else
                    destination set 'Anime';
                if (AnimeTitles has English)
                    if (AnimeTitles has English and Main)
                        subfolder set first(AnimeTitles has English and Main);
                    else if (AnimeTitles has English and Official)
                        subfolder set first(AnimeTitles has English and Official);
                    else
                        subfolder set first(AnimeTitles has English);
                else
                    subfolder set first(AnimeTitles has Main);",
            "[TG] animeTitle1 05v2 episodeTitle1 1080p x.264 8bit DVD [DUAL-AUDIO] [CEN] [ABC123]", "h-anime", "animeTitle1")]
        [DataRow("add AnimeReleaseDate AnimeReleaseDate.Year EpisodeReleaseDate EpisodeReleaseDate.Month FileReleaseDate FileReleaseDate.Day;",
            "2001.01.2020012001.03.0731997.12.022", null, null)]
        public void BigTest(string input, string eFilename, string eDestination, string eSubfolder)
        {
            var parser = Setup(input);
            var context = parser.start();

            var visitor = new ScriptRenamerVisitor
            {
                AnimeInfo = Mock.Of<ISeries>(a =>
                    a.PreferredTitle == "prefTitle" &&
                    a.Restricted &&
                    a.Type == AnimeType.Movie &&
                    a.EpisodeCounts.Episodes == 20 &&
                    a.AirDate == new DateTime(2001, 1, 20) &&
                    a.Titles == new List<AnimeTitle>
                    {
                        new()
                        {
                            Title = "animeTitle1",
                            Language = TitleLanguage.English,
                            Type = TitleType.Main
                        },
                        new()
                        {
                            Title = "animeTitle2",
                            Language = TitleLanguage.Japanese,
                            Type = TitleType.Official
                        },
                        new()
                        {
                            Title = "animeTitle3",
                            Language = TitleLanguage.Romaji,
                            Type = TitleType.Main
                        },
                        new()
                        {
                            Title = "animeTitle4",
                            Language = TitleLanguage.English,
                            Type = TitleType.Main
                        }
                    }
                ),
                FileInfo = Mock.Of<IVideoFile>(file =>
                    file.Video.Hashes == Mock.Of<IHashes>(hashes =>
                        hashes.CRC == "abc123") &&
                    file.Video.MediaInfo == Mock.Of<IMediaInfo>(x =>
                        x.VideoStream == Mock.Of<IVideoStream>(vs =>
                            vs.Resolution == "1080p" &&
                            vs.BitDepth == 8 &&
                            vs.Codec == Mock.Of<IStreamCodecInfo>(c => c.Simplified == "x.264"))) &&
                    file.Video.AniDB == Mock.Of<IAniDBFile>(af =>
                        af.ReleaseGroup == Mock.Of<IReleaseGroup>(rg =>
                            rg.Name == "testGroup" &&
                            rg.ShortName == "TG") &&
                        af.ReleaseDate == new DateTime(1997, 12, 2) &&
                        af.Censored &&
                        af.Source == "DVD" &&
                        af.Version == 2 &&
                        af.MediaInfo == Mock.Of<AniDBMediaData>(md =>
                            md.AudioLanguages == new List<TitleLanguage> { TitleLanguage.English, TitleLanguage.Japanese } &&
                            md.SubLanguages == new List<TitleLanguage>()
                        )
                    )
                ),
                EpisodeInfo = Mock.Of<IEpisode>(e =>
                    e.EpisodeNumber == 5 &&
                    e.Type == EpisodeType.Episode &&
                    e.AirDate == new DateTime(2001, 3, 7) &&
                    e.Titles == new List<AnimeTitle>
                    {
                        new()
                        {
                            Title = "episodeTitle1",
                            Language = TitleLanguage.English,
                            Type = TitleType.Official
                        },
                        new()
                        {
                            Title = "episdoeTitle2",
                            Language = TitleLanguage.Danish,
                            Type = TitleType.Main
                        },
                        new()
                        {
                            Title = "episodeTitle3",
                            Language = TitleLanguage.Romaji,
                            Type = TitleType.Main
                        }
                    }
                ),
                AvailableFolders = new List<IImportFolder>
                {
                    Mock.Of<IImportFolder>(x =>
                        x.DropFolderType == DropFolderType.Source && x.Path == @"C:\Users\Mike\Desktop\Anime Drop" && x.Name == "Drop"),
                    Mock.Of<IImportFolder>(x =>
                        x.DropFolderType == DropFolderType.Destination && x.Path == @"C:\Users\Mike\Desktop\Movies" && x.Name == "Movies"),
                    Mock.Of<IImportFolder>(x => x.DropFolderType == DropFolderType.Both && x.Path == @"C:\Users\Mike\Desktop\Anime" && x.Name == "Anime"),
                    Mock.Of<IImportFolder>(x =>
                        x.DropFolderType == DropFolderType.Destination && x.Path == @"C:\Users\Mike\Desktop\h-anime" && x.Name == "h-anime")
                }
            };
            _ = visitor.Visit(context);
            Assert.AreEqual(eFilename, visitor.Filename);
            visitor.Renaming = false;
            _ = visitor.Visit(context);
            Assert.AreEqual(eDestination, visitor.Destination);
            Assert.AreEqual(eSubfolder, visitor.Subfolder);
        }

        [TestMethod]
        public void TestDanglingElse()
        {
            var parser = Setup(
                @"if (true) if (false) {} else {}");
            var context = parser.start();
            var visitor = new ScriptRenamerVisitor();
            _ = visitor.Visit(context);
            Assert.IsTrue(context.ctrlstmt(0).ctrl().if_stmt().ELSE() is null &&
                          context.ctrlstmt(0).ctrl().if_stmt().true_branch.ctrl().if_stmt().ELSE() is not null);
        }

        [TestMethod]
        public void TestAnimeTypeIs()
        {
            var parser = Setup("if (AnimeType is Movie) {        }");
            var context = parser.if_stmt();
            var visitor = new ScriptRenamerVisitor
            {
                AnimeInfo = Mock.Of<ISeries>(a => a.Type == AnimeType.Movie)
            };
            var result = visitor.Visit(context);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void TestNumberAtomCompare()
        {
            var parser = Setup("if (22 < EpisodeCount) filename add 'testing'; ");
            var context = parser.start();
            var visitor = new ScriptRenamerVisitor
            {
                AnimeInfo = Mock.Of<ISeries>(x =>
                    x.EpisodeCounts.Episodes == 25),
                EpisodeInfo = Mock.Of<IEpisode>(x =>
                    x.Type == EpisodeType.Episode
                ),
            };
            var result = visitor.Visit(context);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void TestStringAtomCompare()
        {
            var parser = Setup("if ('testing' == AnimeTitlePreferred) {}");
            var context = parser.if_stmt().bool_expr();
            var visitor = new ScriptRenamerVisitor
            {
                ShokoSeries = Mock.Of<IShokoSeries>(a => a.PreferredTitle == "testing")
            };
            var result = (bool)visitor.Visit(context);
            Assert.IsTrue(result);
        }

        [DataTestMethod]
        [DataRow("if (DubLanguages has Japanese and not SubLanguages has English) add '[raw] ';\n"
                 + "if (AnimeTitles has English and Main and len(AnimeTitles has Main and English) == 2) filename add AnimeTitles has English and Main;\n"
                 + "if (len(ImportFolders) == 0) add ' empty import folder';\n"
                 + "if (first(AnimeTitles)) add ' ' first(AnimeTitles);\n"
                 + "if (EpisodeTitles has English and Main) add ' ' EpisodeTitles has Main and English;\n",
            "[raw] test, test4 empty import folder test etest, etest4")]
        [DataRow("if (len(DubLanguages)) add len(DubLanguages);", "2")]
        public void TestHasOperator(string input, string expected)
        {
            var parser = Setup(input);
            var context = parser.start();
            var visitor = new ScriptRenamerVisitor
            {
                FileInfo = Mock.Of<IVideoFile>(v =>
                    v.Video == Mock.Of<IVideo>(vf => vf.AniDB == Mock.Of<IAniDBFile>(m =>
                            m.MediaInfo == Mock.Of<AniDBMediaData>(md =>
                                md.AudioLanguages == new List<TitleLanguage> { TitleLanguage.Afrikaans, TitleLanguage.Japanese } &&
                                md.SubLanguages == new List<TitleLanguage> { TitleLanguage.Hebrew, TitleLanguage.Galician }
                            )
                        )
                    )
                ),
                AnimeInfo = Mock.Of<ISeries>(a =>
                    a.Titles == new List<AnimeTitle>
                    {
                        new()
                        {
                            Title = "test",
                            Language = TitleLanguage.English,
                            Type = TitleType.Main
                        },
                        new()
                        {
                            Title = "test2",
                            Language = TitleLanguage.Japanese,
                            Type = TitleType.Official
                        },
                        new()
                        {
                            Title = "test3",
                            Language = TitleLanguage.Romaji,
                            Type = TitleType.Main
                        },
                        new()
                        {
                            Title = "test4",
                            Language = TitleLanguage.English,
                            Type = TitleType.Main
                        }
                    }
                ),
                EpisodeInfo = Mock.Of<IEpisode>(e =>
                    e.Titles == new List<AnimeTitle>
                    {
                        new()
                        {
                            Title = "etest",
                            Language = TitleLanguage.English,
                            Type = TitleType.Main
                        },
                        new()
                        {
                            Title = "etest2",
                            Language = TitleLanguage.Japanese,
                            Type = TitleType.Official
                        },
                        new()
                        {
                            Title = "etest3",
                            Language = TitleLanguage.Romaji,
                            Type = TitleType.Main
                        },
                        new()
                        {
                            Title = "etest4",
                            Language = TitleLanguage.English,
                            Type = TitleType.Main
                        }
                    }
                )
            };
            _ = visitor.Visit(context);
            Assert.AreEqual(expected, visitor.Filename);
        }

        [TestMethod]
        public void TestSetStmt()
        {
            var parser = Setup("filename set 'test' 'testing' 'testing' AnimeTitlePreferred;");
            var context = parser.start();
            var visitor = new ScriptRenamerVisitor
            {
                ShokoSeries = Mock.Of<IShokoSeries>(a => a.PreferredTitle == "wioewoihwoiehwoihweohwiowj")
            };
            _ = visitor.Visit(context);
            Assert.AreEqual("testtestingtestingwioewoihwoiehwoihweohwiowj", visitor.Filename);
        }

        [TestMethod]
        public void TestDynamicEquality()
        {
            var parser = Setup("265252 != 234232 and 'abc' == 'abc' and 1231 == '1231' and 'abd' != 'abdd'");
            var context = parser.bool_expr();
            Assert.IsTrue((bool)new ScriptRenamerVisitor().Visit(context));
        }

        [DataTestMethod]
        [DataRow("if (1 == '1' and 2 <= 2 and true == true and 'true' and 1 and (false and true or true)) add 'true';", "true")]
        [DataRow("if (not (1 and 2 and 0)) add 'true';", "true")]
        [DataRow("if (not(not true or (not true and true and true))) add 'true';", "true")]
        [DataRow("if (not ('test' and 'testing' and '')) add 'true';", "true")]
        public void TestExpression(string input, string expected)
        {
            var parser = Setup(input);
            var context = parser.start();
            var visitor = new ScriptRenamerVisitor();
            _ = visitor.Visit(context);

            Assert.AreEqual(expected, visitor.Filename);
        }

        [TestMethod]
        public void TestLexerError()
        {
            try
            {
                var parser = Setup("if (true or false and true and -++107.2342 == 3) filename add ' '; ");
                var context = parser.start();
                var visitor = new ScriptRenamerVisitor();
                _ = visitor.Visit(context);
                Assert.Fail();
            }
            catch
            {
                // ignored
            }
        }

        [DataTestMethod]
        [DataRow("set 'test';\n destination set 'testdest';\n subfolder set 'subtest';\n skipRename;", null, "testdest", "subtest", null)]
        [DataRow("set 'test';\n destination set 'testdest';\n subfolder set 'subtest';\n skipMove;", "test", null, null, null)]
        [DataRow("set 'test';\n destination set 'testdest';\n subfolder set 'subtest';\n cancel 'canc' 'elex';", null, null, null, "cancelex")]
        public void TestSkipCancel(string input, string eFilename, string eDestination, string eSubfolder, string exMsg)
        {
            try
            {
                var renamer = new ScriptRenamer.ScriptRenamer(Mock.Of<ILogger<ScriptRenamer.ScriptRenamer>>());
                var testFolderPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "C:\\testfld" : "/testfld";
                var result = renamer.GetNewPath(new RelocationEventArgs<ScriptRenamerSettings>
                {
                    MoveEnabled = true,
                    RenameEnabled = true,
                    Settings = new ScriptRenamerSettings { Script = input },
                    Series = new[] { Mock.Of<IShokoSeries>(s => s.AnidbAnime == Mock.Of<ISeries>()) },
                    File = Mock.Of<IVideoFile>(vf => vf.Video == Mock.Of<IVideo>() && vf.Path == Path.Combine(testFolderPath, "testfile.mp4")),
                    Episodes = new[] { Mock.Of<IShokoEpisode>(se => se.AnidbEpisode == Mock.Of<IEpisode>()) },
                    Groups = new[] { Mock.Of<IShokoGroup>() },
                    AvailableFolders = new[]
                        { Mock.Of<IImportFolder>(f => f.Name == "testdest" && f.DropFolderType == DropFolderType.Destination && f.Path == testFolderPath) }
                });
                Assert.AreEqual(result.FileName, eFilename);
                Assert.AreEqual(eDestination, result.DestinationImportFolder?.Name);
                Assert.AreEqual(eSubfolder, result.Path);
            }
            catch (Exception e)
            {
                Assert.IsTrue(e.Message.EndsWith(exMsg));
            }
        }

        [DataTestMethod]
        [DynamicData(nameof(TestEpisodeSelectionAndNameData))]
        public void TestEpisodeSelectionAndName(List<IEpisode> episodes, int eEpisodeId, string eEpisodesString)
        {
            var visitor = new ScriptRenamerVisitor(new RelocationEventArgs<ScriptRenamerSettings>
            {
                RenameEnabled = true,
                AvailableFolders = new List<IImportFolder>(),
                File = Mock.Of<IVideoFile>(),
                Episodes = episodes.Select(e => Mock.Of<IShokoEpisode>(se => se.AnidbEpisode == e)).ToList(),
                Series = new List<IShokoSeries>
                {
                    Mock.Of<IShokoSeries>(s => s.AnidbAnime == Mock.Of<ISeries>(a => a.ID == 10))
                },
                Groups = Array.Empty<IShokoGroup>(),
                Settings = new ScriptRenamerSettings()
            });
            Assert.AreEqual(eEpisodeId, visitor.EpisodeInfo.ID);
            var parser = Setup("add EpisodeNumbers;");
            var context = parser.start();
            _ = visitor.Visit(context);
            Assert.AreEqual(eEpisodesString, visitor.Filename);
        }

        private static IEnumerable<object[]> TestEpisodeSelectionAndNameData
        {
            get
            {
                yield return new object[]
                {
                    new List<IEpisode>
                    {
                        Mock.Of<IEpisode>(e => e.Type == EpisodeType.Episode && e.EpisodeNumber == 1 && e.SeriesID == 10 && e.ID == 1),
                        Mock.Of<IEpisode>(e => e.Type == EpisodeType.Episode && e.EpisodeNumber == 2 && e.SeriesID == 10 && e.ID == 2),
                        Mock.Of<IEpisode>(e => e.Type == EpisodeType.Other && e.EpisodeNumber == 5 && e.SeriesID == 10 && e.ID == 3),
                        Mock.Of<IEpisode>(e => e.Type == EpisodeType.Other && e.EpisodeNumber == 1 && e.SeriesID == 9 && e.ID == 4)
                    },
                    3,
                    "1-2 O5"
                };
                yield return new object[]
                {
                    new List<IEpisode>
                    {
                        Mock.Of<IEpisode>(e => e.Type == EpisodeType.Episode && e.EpisodeNumber == 1 && e.SeriesID == 9 && e.ID == 1),
                        Mock.Of<IEpisode>(e => e.Type == EpisodeType.Episode && e.EpisodeNumber == 2 && e.SeriesID == 10 && e.ID == 2),
                        Mock.Of<IEpisode>(e => e.Type == EpisodeType.Parody && e.EpisodeNumber == 5 && e.SeriesID == 10 && e.ID == 3),
                        Mock.Of<IEpisode>(e => e.Type == EpisodeType.Episode && e.EpisodeNumber == 1 && e.SeriesID == 10 && e.ID == 4),
                        Mock.Of<IEpisode>(e => e.Type == EpisodeType.Special && e.EpisodeNumber == 1 && e.SeriesID == 10 && e.ID == 5),
                        Mock.Of<IEpisode>(e => e.Type == EpisodeType.Credits && e.EpisodeNumber == 2 && e.SeriesID == 10 && e.ID == 6)
                    },
                    4,
                    "1-2 C2 S1 P5"
                };
                yield return new object[]
                {
                    new List<IEpisode>
                    {
                        Mock.Of<IEpisode>(e => e.Type == EpisodeType.Episode && e.EpisodeNumber == 1 && e.SeriesID == 9 && e.ID == 1),
                        Mock.Of<IEpisode>(e => e.Type == EpisodeType.Episode && e.EpisodeNumber == 2 && e.SeriesID == 10 && e.ID == 2),
                        Mock.Of<IEpisode>(e => e.Type == EpisodeType.Parody && e.EpisodeNumber == 5 && e.SeriesID == 10 && e.ID == 3),
                        Mock.Of<IEpisode>(e => e.Type == EpisodeType.Episode && e.EpisodeNumber == 1 && e.SeriesID == 10 && e.ID == 4),
                        Mock.Of<IEpisode>(e => e.Type == EpisodeType.Special && e.EpisodeNumber == 1 && e.SeriesID == 10 && e.ID == 5),
                        Mock.Of<IEpisode>(e => e.Type == EpisodeType.Credits && e.EpisodeNumber == 2 && e.SeriesID == 10 && e.ID == 6),
                        Mock.Of<IEpisode>(e => e.Type == EpisodeType.Episode && e.EpisodeNumber == 3 && e.SeriesID == 10 && e.ID == 4),
                        Mock.Of<IEpisode>(e => e.Type == EpisodeType.Episode && e.EpisodeNumber == 5 && e.SeriesID == 10 && e.ID == 4),
                        Mock.Of<IEpisode>(e => e.Type == EpisodeType.Episode && e.EpisodeNumber == 6 && e.SeriesID == 10 && e.ID == 4),
                        Mock.Of<IEpisode>(e => e.Type == EpisodeType.Special && e.EpisodeNumber == 2 && e.SeriesID == 10 && e.ID == 5),
                        Mock.Of<IEpisode>(e => e.Type == EpisodeType.Special && e.EpisodeNumber == 4 && e.SeriesID == 10 && e.ID == 5),
                        Mock.Of<IEpisode>(e => e.Type == EpisodeType.Episode && e.EpisodeNumber == 8 && e.SeriesID == 10 && e.ID == 4),
                        Mock.Of<IEpisode>(e => e.Type == EpisodeType.Episode && e.EpisodeNumber == 10 && e.SeriesID == 10 && e.ID == 4),
                    },
                    4,
                    "1-3 5-6 8 10 C2 S1-2 S4 P5"
                };
            }
        }

        [DataTestMethod]
        [DataRow("53", "53")]
        [DataRow("'weihrowih' + 'testting'", "weihrowihtestting")]
        [DataRow("substr('blarglargle', 3)", "rglargle")]
        [DataRow("substr('mrglrglelergle', 5, 3)", "gle")]
        [DataRow("substr('blarg', 2, 50)", "arg")]
        [DataRow("substr('blargleargle', 20, 20)", "")]
        [DataRow("substr('argle', 0, 0)", "")]
        [DataRow("substr('aowih', 4, 1)", "h")]
        [DataRow("substr('owhiw', 5)", "")]
        [DataRow("trunc('j 098jwa09f 0we9hwh90h23', 14)", "j 098jwa09f 0w")]
        [DataRow("trunc('j 098jwa09f 0we9hwh90h23', 500)", "j 098jwa09f 0we9hwh90h23")]
        [DataRow("trunc('j 098jwa09f 0we9hwh90h23', 24)", "j 098jwa09f 0we9hwh90h23")]
        [DataRow("trim('  w wihowieh '+'weio'+'hw oowoo     ')", "w wihowieh weiohw oowoo")]
        public void TestStringOperations(string input, string expected)
        {
            var visitor = new ScriptRenamerVisitor();
            var parser = Setup(input);
            var context = parser.string_atom();
            var result = (string)visitor.Visit(context);
            Assert.AreEqual(expected, result);
        }

        [DataTestMethod]
        [DynamicData(nameof(TestLastEpisodeNumberData))]
        public void TestLastEpisodeNumber(List<IEpisode> episodes, string expected)
        {
            var visitor = new ScriptRenamerVisitor(new RelocationEventArgs<ScriptRenamerSettings>
            {
                RenameEnabled = true,
                Settings = new ScriptRenamerSettings(),
                AvailableFolders = Array.Empty<IImportFolder>(),
                File = Mock.Of<IVideoFile>(),
                Episodes = episodes.Select(e => Mock.Of<IShokoEpisode>(se => se.AnidbEpisode == e)).ToList(),
                Series = new List<IShokoSeries>
                {
                    Mock.Of<IShokoSeries>(s => s.AnidbAnime == Mock.Of<ISeries>(a => a.ID == 10))
                },
                Groups = Array.Empty<IShokoGroup>()
            });
            var parser = Setup("add EpisodeNumber '-' LastEpisodeNumber;");
            var context = parser.start();
            _ = visitor.Visit(context);
            Assert.AreEqual(expected, visitor.Filename);
        }

        private static IEnumerable<object[]> TestLastEpisodeNumberData => new[]
        {
            new object[]
            {
                new List<IEpisode>
                {
                    Mock.Of<IEpisode>(e => e.Type == EpisodeType.Episode && e.EpisodeNumber == 1 && e.SeriesID == 10),
                    Mock.Of<IEpisode>(e => e.Type == EpisodeType.Episode && e.EpisodeNumber == 2 && e.SeriesID == 10),
                    Mock.Of<IEpisode>(e => e.Type == EpisodeType.Episode && e.EpisodeNumber == 10 && e.SeriesID == 10),
                    Mock.Of<IEpisode>(e => e.Type == EpisodeType.Episode && e.EpisodeNumber == 3 && e.SeriesID == 10),
                    Mock.Of<IEpisode>(e => e.Type == EpisodeType.Episode && e.EpisodeNumber == 6 && e.SeriesID == 10),
                    Mock.Of<IEpisode>(e => e.Type == EpisodeType.Episode && e.EpisodeNumber == 11 && e.SeriesID == 10),
                    Mock.Of<IEpisode>(e => e.Type == EpisodeType.Episode && e.EpisodeNumber == 15 && e.SeriesID == 10),
                    Mock.Of<IEpisode>(e => e.Type == EpisodeType.Episode && e.EpisodeNumber == 8 && e.SeriesID == 10),
                    Mock.Of<IEpisode>(e => e.Type == EpisodeType.Episode && e.EpisodeNumber == 7 && e.SeriesID == 10),
                    Mock.Of<IEpisode>(e => e.Type == EpisodeType.Episode && e.EpisodeNumber == 9 && e.SeriesID == 10),
                    Mock.Of<IEpisode>(e => e.Type == EpisodeType.Episode && e.EpisodeNumber == 4 && e.SeriesID == 10),
                    Mock.Of<IEpisode>(e => e.Type == EpisodeType.Episode && e.EpisodeNumber == 13 && e.SeriesID == 10),
                    Mock.Of<IEpisode>(e => e.Type == EpisodeType.Episode && e.EpisodeNumber == 14 && e.SeriesID == 10),
                    Mock.Of<IEpisode>(e => e.Type == EpisodeType.Episode && e.EpisodeNumber == 12 && e.SeriesID == 10),
                    Mock.Of<IEpisode>(e => e.Type == EpisodeType.Episode && e.EpisodeNumber == 5 && e.SeriesID == 10),
                },
                "1-15"
            }
        };
    }
}
