using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using LibGit2Sharp;
using Version = System.Version;

namespace OoLunar.ConvenientCompany.Tools.SemVerParser
{
    public static class Program
    {
        private const string _manifestFile = ThisAssembly.Project.ProjectRoot + "/manifest.json";
        private static readonly JsonSerializerOptions _jsonSerializerDefaults = new(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = true
        };

        private static readonly HttpClient _httpClient = new()
        {
            DefaultRequestHeaders =
            {
                { "User-Agent", $"{ThisAssembly.Project.AssemblyName}/{ThisAssembly.Project.Version} ({ThisAssembly.Project.RepositoryUrl})" }
            }
        };

        private static readonly string[] _projectFiles = [
            "icon.png",
            "LICENSE",
            "manifest.json",
            "README.md"
        ];

        public static async Task<int> Main(string[] args)
        {
            Console.WriteLine($"Searching {ThisAssembly.Project.ProjectRoot} for modpack files...");
            IReadOnlyList<string> modpackFiles = FindModpackFiles();
            if (modpackFiles.Count == 0)
            {
                Console.WriteLine("No modpack files found.");
                return 0;
            }

            Console.WriteLine($"Found {modpackFiles.Count} modpack files: ");
            foreach (string modpackFile in modpackFiles)
            {
                Console.WriteLine($"\t- {modpackFile}");
            }

            Console.WriteLine("Parsing mod list...");
            FileStream manifestStream = File.OpenRead(_manifestFile);
            ThunderStoreManifest? manifest = JsonSerializer.Deserialize<ThunderStoreManifest>(manifestStream, _jsonSerializerDefaults);
            await manifestStream.DisposeAsync();
            if (manifest is null)
            {
                Console.WriteLine("Unable to parse manifest file.");
                return 1;
            }

            IReadOnlyList<ThunderStoreMod>? dependencies = ParseDependencies(manifest);
            if (dependencies is null)
            {
                Console.WriteLine("Unable to parse dependencies.");
                return 1;
            }
            else if (dependencies.Count == 0)
            {
                Console.WriteLine("No mods found.");
                return 0;
            }

            IReadOnlyList<ThunderStoreMod> addedMods = GetAddedMods(dependencies);
            IReadOnlyList<ThunderStoreMod> removedMods = GetRemovedMods(dependencies);
            if (args.Contains("--just-changelog"))
            {
                WriteChangelog(addedMods, removedMods, new Dictionary<ThunderStoreMod, Version>());
                return 0;
            }

            Console.WriteLine($"Checking for updates for {manifest.Dependencies.Count} mods...");
            IReadOnlyDictionary<ThunderStoreMod, Version> updatedMods = await CheckForUpdatesAsync(dependencies);
            if (addedMods.Count == 0 && removedMods.Count == 0 && updatedMods.Count == 0)
            {
                Console.WriteLine("No updates found.");
                return 0;
            }

            // Bump either the minor or patch version of the modpack. Major bumps will always be made manually.
            Version updatedModpackVersion = BumpVersion(manifest.VersionNumber, addedMods, removedMods, updatedMods);

            Console.WriteLine($"Found updates. Updating modpack from {manifest.VersionNumber} to {updatedModpackVersion}...");
            List<string> updatedDependencies = [];
            foreach (ThunderStoreMod mod in dependencies)
            {
                if (updatedMods.TryGetValue(mod, out Version? newVersion))
                {
                    updatedDependencies.Add($"{mod.Author}-{mod.ModName}-{newVersion}");
                }
                else
                {
                    updatedDependencies.Add($"{mod.Author}-{mod.ModName}-{mod.Version}");
                }
            }

            // Ensure the deps are always alphabetically sorted.
            updatedDependencies.Sort();
            UpdateManifestFile(manifest, updatedModpackVersion, updatedDependencies);

            Console.WriteLine("Writing changelog...");
            WriteChangelog(addedMods, removedMods, updatedMods);

            Console.WriteLine("Generating modpack file...");
            GenerateModpackFile(manifest, updatedModpackVersion, modpackFiles);

            Console.WriteLine("Done!");
            Console.WriteLine($"A total of {addedMods.Count} mods were added, {removedMods.Count} mods were removed, and {updatedMods.Count} mods were updated.");
            return 0;
        }

        private static IReadOnlyList<string> FindModpackFiles()
        {
            List<string> projectFiles = [];
            foreach (string fileName in Directory.GetFiles(ThisAssembly.Project.ProjectRoot))
            {
                if (_projectFiles.Contains(Path.GetFileName(fileName)))
                {
                    projectFiles.Add(fileName);
                }
            }

            return projectFiles;
        }

        private static IReadOnlyList<ThunderStoreMod>? ParseDependencies(ThunderStoreManifest manifest)
        {
            List<ThunderStoreMod> dependencies = [];
            foreach (string dependency in manifest.Dependencies)
            {
                string[] modProperties = dependency.Split('-');
                if (modProperties.Length != 3)
                {
                    Console.WriteLine($"Unable to parse mod version from {dependency}");
                    return null;
                }

                dependencies.Add(new()
                {
                    Author = modProperties[0],
                    ModName = modProperties[1],
                    Version = Version.Parse(modProperties[2])
                });
            }

            return dependencies;
        }

        private static IReadOnlyList<ThunderStoreMod> GetAddedMods(IReadOnlyList<ThunderStoreMod> dependencies)
        {
            List<ThunderStoreMod> addedMods = [];
            Repository repository = new(ThisAssembly.Project.ProjectRoot + "/.git");

            // Compare latest commit to the last commit
            Commit latestCommit = repository.Head.Tip;
            Commit lastCommit = latestCommit.Parents.First();
            TreeChanges treeChanges = repository.Diff.Compare<TreeChanges>(lastCommit.Tree, latestCommit.Tree);

            // Check to see if the manifest file has changed.
            if (!treeChanges.Any(change => change.Path == "manifest.json"))
            {
                return addedMods;
            }

            // If it has, we need to compare the old manifest file to the new one.
            ThunderStoreManifest? manifest = JsonSerializer.Deserialize<ThunderStoreManifest>(((Blob)lastCommit["manifest.json"].Target).GetContentText(), _jsonSerializerDefaults);
            if (manifest is null)
            {
                Console.WriteLine("Unable to parse old manifest file.");
                return [];
            }

            // And then parse the dependencies from the new manifest file.
            IReadOnlyList<ThunderStoreMod>? oldDependencies = ParseDependencies(manifest);
            if (oldDependencies is null)
            {
                Console.WriteLine("Unable to parse dependencies.");
                return [];
            }

            // Finally, compare the old dependencies to the new ones to find the added mods.
            addedMods.AddRange(dependencies.Where(mod => oldDependencies.All(oldMod => mod.Author != oldMod.Author && mod.ModName != oldMod.ModName)));

            return addedMods;
        }

        private static IReadOnlyList<ThunderStoreMod> GetRemovedMods(IReadOnlyList<ThunderStoreMod> dependencies)
        {
            List<ThunderStoreMod> removedMods = [];
            Repository repository = new(ThisAssembly.Project.ProjectRoot + "/.git");

            // Compare latest commit to the last commit
            Commit latestCommit = repository.Head.Tip;
            Commit lastCommit = latestCommit.Parents.First();
            TreeChanges treeChanges = repository.Diff.Compare<TreeChanges>(lastCommit.Tree, latestCommit.Tree);

            // Check to see if the manifest file has changed.
            if (!treeChanges.Any(change => change.Path == "manifest.json"))
            {
                return removedMods;
            }

            // If it has, we need to compare the old manifest file to the new one.
            ThunderStoreManifest? manifest = JsonSerializer.Deserialize<ThunderStoreManifest>(((Blob)lastCommit["manifest.json"].Target).GetContentText(), _jsonSerializerDefaults);
            if (manifest is null)
            {
                Console.WriteLine("Unable to parse old manifest file.");
                return new List<ThunderStoreMod>();
            }

            // And then parse the dependencies from the old manifest file.
            IReadOnlyList<ThunderStoreMod>? oldDependencies = ParseDependencies(manifest);
            if (oldDependencies is null)
            {
                Console.WriteLine("Unable to parse dependencies.");
                return new List<ThunderStoreMod>();
            }

            // Finally, compare the new dependencies to the old ones to find the removed mods.
            removedMods.AddRange(oldDependencies.Where(oldMod => dependencies.All(mod => mod.Author != oldMod.Author && mod.ModName != oldMod.ModName)));

            return removedMods;
        }

        private static async ValueTask<Dictionary<ThunderStoreMod, Version>> CheckForUpdatesAsync(IReadOnlyList<ThunderStoreMod> mods)
        {
            Dictionary<ThunderStoreMod, Version> modUpdates = [];
            foreach (ThunderStoreMod mod in mods)
            {
                HttpResponseMessage responseMessage;
                do
                {
                    responseMessage = await _httpClient.GetAsync($"https://thunderstore.io/api/experimental/package/{mod.Author}/{mod.ModName}/");
                    if (responseMessage.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        Console.WriteLine("Hit the ratelimit. Waiting 15 seconds...");
                        await Task.Delay(TimeSpan.FromSeconds(15));
                    }
                } while (responseMessage.StatusCode == HttpStatusCode.TooManyRequests);

                ThunderStorePackageListing? packageListing = await responseMessage.Content.ReadFromJsonAsync<ThunderStorePackageListing>(_jsonSerializerDefaults);
                if (packageListing is null)
                {
                    Console.WriteLine($"Unable to find mod {mod.ModName} by {mod.Author} on ThunderStore. Skipping update check.");
                }
                else if (!Version.TryParse(packageListing.Latest.VersionNumber, out Version? updatedModVersion))
                {
                    Console.WriteLine($"Unable to parse version number {packageListing.Latest.VersionNumber} for mod {mod.ModName} by {mod.Author}. Skipping update check.");
                }
                else if (updatedModVersion > mod.Version)
                {
                    Console.WriteLine($"Found update for mod {mod.ModName} by {mod.Author} from {mod.Version} to {updatedModVersion}.");
                    modUpdates.Add(mod, updatedModVersion);
                }
            }

            return modUpdates;
        }

        private static Version BumpVersion(Version modpackVersion, IReadOnlyList<ThunderStoreMod> addedMods, IReadOnlyList<ThunderStoreMod> removedMods, IReadOnlyDictionary<ThunderStoreMod, Version> modUpdates)
        {
            if (addedMods.Count != 0 || removedMods.Count != 0)
            {
                return new Version(modpackVersion.Major, modpackVersion.Minor + 1, 0);
            }

            foreach ((ThunderStoreMod mod, Version newVersion) in modUpdates)
            {
                if (mod.Version.Major < newVersion.Major || mod.Version.Minor < newVersion.Minor)
                {
                    return new Version(modpackVersion.Major, modpackVersion.Minor + 1, 0);
                }
            }

            return new Version(modpackVersion.Major, modpackVersion.Minor, modpackVersion.Build + 1);
        }

        private static void UpdateManifestFile(ThunderStoreManifest manifest, Version updatedModpackVersion, IReadOnlyList<string> updatedDependencies)
        {
            using FileStream manifestStream = File.Open(_manifestFile, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);

            // Clear the contents of the file.
            manifestStream.SetLength(0);

            // Update the dependencies and write the new manifest to the file.
            manifest = manifest with
            {
                VersionNumber = updatedModpackVersion,
                Dependencies = updatedDependencies
            };

            JsonSerializer.Serialize(manifestStream, manifest, _jsonSerializerDefaults);
        }

        private static void WriteChangelog(IReadOnlyList<ThunderStoreMod> addedMods, IReadOnlyList<ThunderStoreMod> removedMods, IReadOnlyDictionary<ThunderStoreMod, Version> updatedDependencies)
        {
            StringBuilder changelogBuilder = new();
            if (addedMods.Count != 0)
            {
                changelogBuilder.AppendLine("## Added Mods\n");
                foreach (ThunderStoreMod mod in addedMods)
                {
                    changelogBuilder.AppendLine($"- `{mod.ModName}` by `{mod.Author}` at `{mod.Version}`");
                }

                changelogBuilder.AppendLine();
            }

            if (removedMods.Count != 0)
            {
                changelogBuilder.AppendLine("## Removed Mods");
                foreach (ThunderStoreMod mod in removedMods)
                {
                    changelogBuilder.AppendLine($"- `{mod.ModName}` by `{mod.Author}` at `{mod.Version}`");
                }

                changelogBuilder.AppendLine();
            }

            if (updatedDependencies.Count != 0)
            {
                changelogBuilder.AppendLine("## Updated Mods");
                foreach ((ThunderStoreMod mod, Version newVersion) in updatedDependencies)
                {
                    changelogBuilder.AppendLine($"- `{mod.ModName}` by `{mod.Author}` from `{mod.Version}` to `{newVersion}`");
                }
            }

            if (addedMods.Count == 0 && removedMods.Count == 0 && updatedDependencies.Count == 0)
            {
                changelogBuilder.AppendLine("No changes made to the modlist.");
            }

            using FileStream changelogStream = File.OpenWrite($"{ThisAssembly.Project.ProjectRoot}/CHANGELOG.md");
            changelogStream.SetLength(0);
            changelogStream.Write(Encoding.UTF8.GetBytes(changelogBuilder.ToString()));
        }

        private static void GenerateModpackFile(ThunderStoreManifest manifest, Version updatedModpackVersion, IReadOnlyList<string> modpackFiles)
        {
            string modpackFileName = $"{ThisAssembly.Project.ProjectRoot}/{manifest.Name}-{updatedModpackVersion}.zip";
            if (File.Exists(modpackFileName))
            {
                File.Delete(modpackFileName);
            }

            using FileStream modpackStream = File.OpenWrite(modpackFileName);
            using ZipArchive modpackArchive = new(modpackStream, ZipArchiveMode.Create);
            foreach (string modpackFile in modpackFiles)
            {
                modpackArchive.CreateEntryFromFile(modpackFile, Path.GetFileName(modpackFile), CompressionLevel.SmallestSize);
            }

            modpackArchive.CreateEntryFromFile($"{ThisAssembly.Project.ProjectRoot}/CHANGELOG.md", "CHANGELOG.md", CompressionLevel.SmallestSize);
        }
    }
}