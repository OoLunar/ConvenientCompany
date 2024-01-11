using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using LibGit2Sharp;
using Version = System.Version;

namespace OoLunar.ConvenientCompany.Tools.SemVerParser
{
    public sealed class GitTools
    {
        public static readonly Repository _repository = new(ThisAssembly.Project.ProjectRoot + "/.git");

        public static IReadOnlyList<ThunderStoreMod> GetLastPublishedModList()
        {
            // Compare latest commit to the latest tag.
            Commit latestCommit = _repository.Head.Tip;
            Commit? latestTag = null;
            foreach (Tag tag in _repository.Tags)
            {
                // Ensure the tag is a commit and that it is older than the head commit.
                // Also ensure that the tag is newer than the last found tag.
                if (tag.Target is Commit commit
                    && commit.Author.When < latestCommit.Author.When
                    && (latestTag is null || commit.Author.When > latestTag.Author.When))
                {
                    latestTag = commit;
                }
            }

            // If no tag was found, return an empty list.
            // This means that no mods were removed or updated.
            if (latestTag is null)
            {
                return [];
            }

            // Compare the old manifest file to the new one.
            ThunderStoreManifest manifest = JsonSerializer
                .Deserialize<ThunderStoreManifest>(((Blob)latestTag["manifest.json"].Target).GetContentText(), Program.JsonSerializerDefaults)
                .NullPanic($"Unable to parse manifest file on tag {latestTag.Id}.");

            // And then parse the dependencies from the new manifest file.
            return Program.ParseDependencies(manifest).NullPanic($"Unable to parse dependencies from manifest file on tag {latestTag.Id}.");
        }

        // If the new dependencies contain a mod that is not in the old dependencies, it was added.
        public static IReadOnlyList<ThunderStoreMod> GetAddedMods(IReadOnlyList<ThunderStoreMod> dependencies) => dependencies
            .Except(GetLastPublishedModList(), new ThunderStoreModEqualityComparer())
            .ToList();

        // If the old dependencies contain a mod that is not in the new dependencies, it was removed.
        public static IReadOnlyList<ThunderStoreMod> GetRemovedMods(IReadOnlyList<ThunderStoreMod> dependencies) => GetLastPublishedModList()
            .Where(oldMod => dependencies.All(mod => mod.Author != oldMod.Author && mod.ModName != oldMod.ModName))
            .ToList();

        public static IReadOnlyDictionary<ThunderStoreMod, Version> GetLocalModUpdates(IReadOnlyList<ThunderStoreMod> dependencies)
        {
            // The dictionary will contain the old mod while the value will contain the new version.
            IReadOnlyList<ThunderStoreMod> oldDependencies = GetLastPublishedModList();
            Dictionary<ThunderStoreMod, Version> updatedMods = [];
            foreach (ThunderStoreMod mod in dependencies)
            {
                // Null when the mod is recently added.
                ThunderStoreMod? oldMod = oldDependencies.FirstOrDefault(oldMod => oldMod.Author == mod.Author && oldMod.ModName == mod.ModName);
                if (oldMod is not null && mod.Version != oldMod.Version)
                {
                    updatedMods.Add(oldMod, mod.Version);
                }
            }

            return updatedMods;
        }
    }
}