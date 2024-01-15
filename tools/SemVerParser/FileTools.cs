using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using OoLunar.ConvenientCompany.Tools.SemVerParser.Entities;
using OoLunar.ConvenientCompany.Tools.SemVerParser.Entities.Api;

namespace OoLunar.ConvenientCompany.Tools.SemVerParser
{
    public sealed class FileTools
    {
        private const string MANIFEST_FILE = ThisAssembly.Project.ProjectRoot + "/manifest.json";
        private static readonly string[] ProjectFiles = [
            "icon.png",
            "LICENSE",
            "manifest.json",
            "README.md"
        ];

        public static async ValueTask<ThunderStoreManifest> ParseManifestFileAsync()
        {
            try
            {
                await using FileStream manifestStream = File.OpenRead(MANIFEST_FILE);
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
                Dependencies = updatedDependencies.OrderBy(x => x.Author, StringComparer.OrdinalIgnoreCase).ToList()
            };

            JsonSerializer.Serialize(manifestStream, manifest, Program.JsonSerializerDefaults);
        }

        public static void WriteChangelog(IReadOnlyDictionary<LocalMod, LocalModAction> modStatuses)
        {
            StringBuilder changelogBuilder = new();
            if (modStatuses.Count == 0)
            {
                changelogBuilder.AppendLine("No changes made to the modlist.");
            }
            else
            {
                foreach (var group in modStatuses.OrderBy(x => x.Key.ModName).GroupBy(x => x.Value))
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
                            if (mod.LatestVersion is not null)
                            {
                                changelogBuilder.AppendLine($"- `{mod.ModName}` by `{mod.Author}` from `{mod.Version}` to `{mod.LatestVersion}`");
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
    }
}