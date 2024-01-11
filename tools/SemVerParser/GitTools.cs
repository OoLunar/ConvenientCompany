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
            // Compare latest commit to the last commit
            Commit headCommit = _repository.Head.Tip;
            Commit lastTag = _repository.Tags
                .OrderByDescending(tag => tag.Target.Peel<Commit>().Author.When)
                .First(tag => tag.Target.Peel<Commit>().Author.When < headCommit.Author.When)
                .Target.Peel<Commit>();

            TreeChanges treeChanges = _repository.Diff.Compare<TreeChanges>(lastTag.Tree, headCommit.Tree);

            // Compare the old manifest file to the new one.
            ThunderStoreManifest manifest = JsonSerializer
                .Deserialize<ThunderStoreManifest>(((Blob)lastTag["manifest.json"].Target).GetContentText(), Program.JsonSerializerDefaults)
                .ExpectNullable($"Unable to parse manifest file on tag {lastTag.Id}.");

            // And then parse the dependencies from the new manifest file.
            return Program.ParseDependencies(manifest)
                .ExpectNullable($"Unable to parse dependencies from manifest file on tag {lastTag.Id}.")
                .OrderByDescending(mod => mod.ModName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // If the new dependencies contain a mod that is not in the old dependencies, it was added.
        public static IReadOnlyList<ThunderStoreMod> GetAddedMods(IReadOnlyList<ThunderStoreMod> dependencies) => GetLastPublishedModList()
            .Where(oldMod => !dependencies.Any(mod => mod.Author == oldMod.Author && mod.ModName == oldMod.ModName))
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