using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using LibGit2Sharp;
using OoLunar.ConvenientCompany.Tools.SemVerParser.Entities;
using OoLunar.ConvenientCompany.Tools.SemVerParser.Entities.Api;
using Version = System.Version;

namespace OoLunar.ConvenientCompany.Tools.SemVerParser
{
    public static class Program
    {
        public const string HTTP_AGENT = $"{ThisAssembly.Project.AssemblyName}/{ThisAssembly.Project.Version} ({ThisAssembly.Project.RepositoryUrl})";
        public static readonly JsonSerializerOptions JsonSerializerDefaults = new(System.Text.Json.JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = true
        };

        public static async Task<int> Main(string[] args)
        {
            if (args.Contains("--help") || args.Contains("-h"))
            {
                Console.WriteLine("Usage: SemVerParser [options]");
                Console.WriteLine("Options:");
                Console.WriteLine("  --help, -h            Show this help message and exit.");
                Console.WriteLine("  --version, -v         Show the version number and exit.");
                Console.WriteLine("  --all-changelogs      Generate changelogs for all versions/releases of the modpack.");
                Console.WriteLine("  --just-changelog      Generate a changelog for the latest release of the modpack.");
                return 0;
            }
            else if (args.Contains("--version") || args.Contains("-v"))
            {
                Console.WriteLine(ThisAssembly.Project.Version);
                return 0;
            }
            else if (args.Contains("--all-changelogs"))
            {
                await WriteAllChangelogsAsync();
                return 0;
            }

            ThunderStoreManifest manifest = FileTools.ParseManifestFile();
            IReadOnlyDictionary<LocalMod, LocalModAction> modStatuses = ThunderStoreTools.GetLocalModDiff(manifest, GitTools.GetLastPublishedManifest());
            if (args.Contains("--just-changelog"))
            {
                await FileTools.WriteChangelogAsync(modStatuses);
                return 0;
            }

            modStatuses = await ThunderStoreTools.CheckForRemoteUpdatesAsync(modStatuses);

            // Bump either the minor or patch version of the modpack. Major bumps must be done manually.
            Version updatedModpackVersion = BumpVersion(modStatuses, manifest, GitTools.GetLastCommitManifest());
            Console.WriteLine(manifest.VersionNumber == updatedModpackVersion
                ? "No updates found."
                : $"Found updates. Updating modpack from {manifest.VersionNumber} to {updatedModpackVersion}..."
            );

            FileTools.UpdateManifestFile(manifest, updatedModpackVersion, modStatuses
                .Where(x => x.Value is not LocalModAction.Uninstall)
                .Select(x => x.Key)
                .OrderBy(x => x.Author, StringComparer.OrdinalIgnoreCase)
                .ToList()
            );
            await FileTools.WriteChangelogAsync(modStatuses);
            FileTools.GenerateModpackFile(manifest, updatedModpackVersion);

            int addedMods = modStatuses.Count(x => x.Value == LocalModAction.Install);
            int removedMods = modStatuses.Count(x => x.Value == LocalModAction.Uninstall);
            int updatedMods = modStatuses.Count(x => x.Value == LocalModAction.Upgrade);
            int downgradedMods = modStatuses.Count(x => x.Value == LocalModAction.Downgrade);
            Console.WriteLine($"Added {addedMods} mods, removed {removedMods} mods, updated {updatedMods} mods, and downgraded {downgradedMods} mods with a total of {modStatuses.Count} mods installed.");
            return 0;
        }

        private static async ValueTask WriteAllChangelogsAsync()
        {
            IReadOnlyList<Tag> tags = GitTools.GetTags();
            for (int i = 0; i < tags.Count; i++)
            {
                Tag tag = tags[i];
                ThunderStoreManifest manifest = JsonSerializer
                    .Deserialize<ThunderStoreManifest>(tag
                        .Target
                        .Peel<Commit>()["manifest.json"]
                        .Target
                        .Peel<Blob>()
                        .GetContentText(), JsonSerializerDefaults)
                    .NullPanic($"Unable to parse manifest file on tag {tag.FriendlyName}.");

                ThunderStoreManifest? lastPublishedManifest = null;
                if (i != 0)
                {
                    lastPublishedManifest = GitTools.GetLastPublishedManifest(tag);
                }

                IReadOnlyDictionary<LocalMod, LocalModAction> modStatuses = ThunderStoreTools.GetLocalModDiff(manifest, lastPublishedManifest);
                await FileTools.WriteChangelogAsync(modStatuses, $"changelog-{tag.FriendlyName}.md");
            }
        }

        private static Version BumpVersion(IReadOnlyDictionary<LocalMod, LocalModAction> modStatuses, ThunderStoreManifest currentManifest, ThunderStoreManifest? lastPublishedManifest = null)
        {
            if (lastPublishedManifest is not null && currentManifest.Dependencies.SequenceEqual(lastPublishedManifest.Dependencies, LocalModVersionEqualityComparer.Instance))
            {
                return currentManifest.VersionNumber;
            }

            foreach ((LocalMod mod, LocalModAction action) in modStatuses)
            {
                if (action == LocalModAction.Install
                    || action == LocalModAction.Uninstall
                    || mod.TrueVersion is null // Someone didn't follow semver. Bump the minor version.
                    || mod.LatestVersion?.Major != mod.TrueVersion.Major // *1*.2.0
                    || mod.LatestVersion?.Minor != mod.TrueVersion.Minor) // 1.*2*.0
                {
                    return new Version(currentManifest.VersionNumber.Major, currentManifest.VersionNumber.Minor + 1, 0);
                }
            }

            return new Version(currentManifest.VersionNumber.Major, currentManifest.VersionNumber.Minor, currentManifest.VersionNumber.Build + 1);
        }
    }
}