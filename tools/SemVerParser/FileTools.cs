using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OoLunar.ConvenientCompany.Tools.SemVerParser.Entities;
using OoLunar.ConvenientCompany.Tools.SemVerParser.Entities.Api;

namespace OoLunar.ConvenientCompany.Tools.SemVerParser
{
    public sealed partial class FileTools
    {
        private const string MANIFEST_FILE = ThisAssembly.Project.ProjectRoot + "/manifest.json";
        private static readonly string[] ProjectFiles = [
            "icon.png",
            "LICENSE",
            "manifest.json",
            "README.md"
        ];

        public static ThunderStoreManifest ParseManifestFile()
        {
            try
            {
                using FileStream manifestStream = File.OpenRead(MANIFEST_FILE);
                return JsonSerializer
                    .Deserialize<ThunderStoreManifest>(manifestStream, Program.JsonSerializerDefaults)
                    .NullPanic("Unable to parse manifest file.");
            }
            catch (Exception error)
            {
                Console.WriteLine(error);
                Environment.Exit(1);
                return null;
            }
        }

        public static void UpdateManifestFile(ThunderStoreManifest manifest, Version updatedModpackVersion, IReadOnlyList<LocalMod> updatedDependencies)
        {
            using FileStream manifestStream = File.Open(MANIFEST_FILE, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);

            // Clear the contents of the file.
            manifestStream.SetLength(0);

            // Update the dependencies and write the new manifest to the file.
            manifest = manifest with
            {
                VersionNumber = updatedModpackVersion,
                Dependencies = updatedDependencies
            };

            JsonSerializer.Serialize(manifestStream, manifest, Program.JsonSerializerDefaults);
        }

        public static async ValueTask WriteChangelogAsync(IReadOnlyDictionary<LocalMod, LocalModAction> modStatuses)
        {
            StringBuilder changelogBuilder = new();
            if (modStatuses.Count == 0)
            {
                changelogBuilder.AppendLine("No changes made to the modlist.");
            }
            else
            {
                foreach (var group in modStatuses.GroupBy(x => x.Value).OrderBy(x => x.Key))
                {
                    if (group.Key is LocalModAction.None)
                    {
                        continue;
                    }

                    changelogBuilder.AppendLine(group.Key switch
                    {
                        LocalModAction.Install => "## Added Mods",
                        LocalModAction.Uninstall => "## Removed Mods",
                        LocalModAction.Upgrade => "## Updated Mods",
                        LocalModAction.Downgrade => "## Downgraded Mods",
                        _ => "## Other Changes"
                    });

                    if (group.Key is LocalModAction.Install or LocalModAction.Uninstall)
                    {
                        foreach ((LocalMod mod, LocalModAction action) in group)
                        {
                            changelogBuilder.AppendLine($"- `{mod.ModName}` by `{mod.Author}`");
                        }
                    }
                    else
                    {
                        foreach ((LocalMod mod, LocalModAction action) in group)
                        {
                            if (mod.LatestVersion is null || mod.LatestVersion == mod.Version)
                            {
                                continue;
                            }
                            else if ((await ThunderStoreTools.GetChangelogAsync(mod)) is ThunderStoreChangelogOrReadMeResponse changelogResponse && !string.IsNullOrWhiteSpace(changelogResponse.Markdown))
                            {
                                changelogBuilder.AppendLine($"- `{mod.ModName}` by `{mod.Author}` from `{mod.Version}` to [`{mod.LatestVersion}`](https://thunderstore.io/c/lethal-company/p/{mod.Author}/{mod.ModName}/changelog/)");
                                ParseChangelogFile(mod, changelogResponse.Markdown, changelogBuilder);
                            }
                            else if ((await ThunderStoreTools.GetReadMeAsync(mod)) is ThunderStoreChangelogOrReadMeResponse readMeResponse && !string.IsNullOrWhiteSpace(readMeResponse.Markdown))
                            {
                                changelogBuilder.AppendLine($"- `{mod.ModName}` by `{mod.Author}` from `{mod.Version}` to [`{mod.LatestVersion}`](https://thunderstore.io/c/lethal-company/p/{mod.Author}/{mod.ModName}/)");
                                ParseChangelogFile(mod, readMeResponse.Markdown, changelogBuilder);
                            }
                            else if ((await ThunderStoreTools.GetWebsiteUrlAsync(mod)) is Uri websiteUrl && (await GitTools.GetReleaseAsync(websiteUrl, mod.LatestVersion)) is (Uri releaseUrl, string releaseBody))
                            {
                                changelogBuilder.AppendLine($"- `{mod.ModName}` by `{mod.Author}` from `{mod.Version}` to [`{mod.LatestVersion}`]({releaseUrl})");
                                if (!string.IsNullOrWhiteSpace(releaseBody))
                                {
                                    ParseChangelogFile(mod, releaseBody, changelogBuilder);
                                }
                                else
                                {
                                    changelogBuilder.AppendLine("\t> No changelog was provided.");
                                }
                            }
                            else
                            {
                                // We were unable to parse the changelog - likely because it was put somewhere unconventional. Like on the README.
                                changelogBuilder.AppendLine($"- `{mod.ModName}` by `{mod.Author}` from `{mod.Version}` to [`{mod.LatestVersion}`](https://thunderstore.io/c/lethal-company/p/{mod.Author}/{mod.ModName}/changelog/)");
                                changelogBuilder.AppendLine("\t> No changelog was provided.");
                            }
                        }
                    }
                }
            }

            using FileStream changelogStream = File.OpenWrite($"{ThisAssembly.Project.ProjectRoot}/CHANGELOG.md");
            changelogStream.SetLength(0);
            changelogStream.Write(Encoding.UTF8.GetBytes(changelogBuilder.ToString()));
        }

        public static void GenerateModpackFile(ThunderStoreManifest manifest, Version updatedModpackVersion)
        {
            string modpackFileName = $"{ThisAssembly.Project.ProjectRoot}/{manifest.Name}-{updatedModpackVersion}.zip";
            if (File.Exists(modpackFileName))
            {
                File.Delete(modpackFileName);
            }

            using FileStream modpackStream = File.OpenWrite(modpackFileName);
            using ZipArchive modpackArchive = new(modpackStream, ZipArchiveMode.Create);
            foreach (string fileName in Directory.GetFiles(ThisAssembly.Project.ProjectRoot))
            {
                if (!ProjectFiles.Contains(Path.GetFileName(fileName)))
                {
                    continue;
                }

                modpackArchive.CreateEntryFromFile(fileName, Path.GetFileName(fileName), CompressionLevel.SmallestSize);
            }

            modpackArchive.CreateEntryFromFile($"{ThisAssembly.Project.ProjectRoot}/CHANGELOG.md", "CHANGELOG.md", CompressionLevel.SmallestSize);
        }

        private static void ParseChangelogFile(LocalMod mod, string changelog, StringBuilder changelogBuilder)
        {
            StringBuilder changelogSectionBuilder = new();
            string[] changelogLines = changelog.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            bool foundLatestVersionFirst = false;
            bool foundOldestVersionFirst = false;
            int latestVersionLine = 0;
            int oldestVersionLine = 0;
            for (int i = 0; i < changelogLines.Length; i++)
            {
                string line = changelogLines[i];

                // Grab the contents between the two versions
                // Match each line to see if we can find a semver match
                Match semVerMatch = GetVersionRegex().Match(line);
                string major = string.IsNullOrWhiteSpace(semVerMatch.Groups[1].Value) ? "0" : semVerMatch.Groups[1].Value;
                string minor = string.IsNullOrWhiteSpace(semVerMatch.Groups[2].Value) ? "0" : semVerMatch.Groups[2].Value;
                string patch = string.IsNullOrWhiteSpace(semVerMatch.Groups[3].Value) ? "0" : semVerMatch.Groups[3].Value;
                if (semVerMatch.Success && Version.TryParse($"{major}.{minor}.{patch}", out Version? semVer))
                {
                    if (semVer == mod.LatestVersion)
                    {
                        latestVersionLine = i;
                        if (foundOldestVersionFirst)
                        {
                            oldestVersionLine = changelogLines.Length;
                            break;
                        }

                        foundLatestVersionFirst = true;
                    }
                    else if (semVer == mod.Version)
                    {
                        oldestVersionLine = i;
                        if (foundLatestVersionFirst)
                        {
                            break;
                        }

                        foundOldestVersionFirst = true;
                    }
                }
            }

            if (foundLatestVersionFirst || foundOldestVersionFirst)
            {
                foreach (string line in changelogLines[latestVersionLine..oldestVersionLine])
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        changelogSectionBuilder.AppendLine($"\t> {line}");
                    }
                }
            }
            else
            {
                for (int i = 0; i < changelogLines.Length; i++)
                {
                    string line = changelogLines[i];
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        changelogSectionBuilder.AppendLine($"\t> {changelogLines[i]}");
                    }
                }
            }

            changelogBuilder.Append(changelogSectionBuilder);
        }

        [GeneratedRegex(@"v?(\d+)\.?(\d+)?\.?(\d+)?", RegexOptions.Compiled | RegexOptions.ECMAScript | RegexOptions.IgnoreCase)]
        private static partial Regex GetVersionRegex();
    }
}