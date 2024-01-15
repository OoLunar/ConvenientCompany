﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using OoLunar.ConvenientCompany.Tools.SemVerParser.Entities;
using OoLunar.ConvenientCompany.Tools.SemVerParser.Entities.Api;

namespace OoLunar.ConvenientCompany.Tools.SemVerParser
{
    public static class Program
    {
        public static readonly JsonSerializerOptions JsonSerializerDefaults = new(System.Text.Json.JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = true
        };

        public static async Task<int> Main(string[] args)
        {
            ThunderStoreManifest manifest = await FileTools.ParseManifestFileAsync();
            IReadOnlyDictionary<LocalMod, LocalModAction> modStatuses = ThunderStoreTools.GetLocalModDiff(manifest, GitTools.GetLastPublishedManifest());
            if (args.Contains("--just-changelog"))
            {
                FileTools.WriteChangelog(modStatuses);
                return 0;
            }

            modStatuses = await ThunderStoreTools.CheckForRemoteUpdatesAsync(modStatuses);

            // Bump either the minor or patch version of the modpack. Major bumps must be done manually.
            Version updatedModpackVersion = BumpVersion(manifest.VersionNumber, modStatuses);
            Console.WriteLine($"Found updates. Updating modpack from {manifest.VersionNumber} to {updatedModpackVersion}...");
            FileTools.UpdateManifestFile(manifest, updatedModpackVersion, modStatuses.Keys.ToList());
            FileTools.WriteChangelog(modStatuses);
            FileTools.GenerateModpackFile(manifest, updatedModpackVersion);

            int addedMods = modStatuses.Count(x => x.Value == LocalModAction.Install);
            int removedMods = modStatuses.Count(x => x.Value == LocalModAction.Uninstall);
            int updatedMods = modStatuses.Count(x => x.Value == LocalModAction.Upgrade);
            int downgradedMods = modStatuses.Count(x => x.Value == LocalModAction.Downgrade);
            Console.WriteLine($"Added {addedMods} mods, removed {removedMods} mods, updated {updatedMods} mods, and downgraded {downgradedMods} mods with a total of {modStatuses.Count} mods installed.");
            return 0;
        }

        private static Version BumpVersion(Version modpackVersion, IReadOnlyDictionary<LocalMod, LocalModAction> modStatuses)
        {
            foreach ((LocalMod mod, LocalModAction action) in modStatuses)
            {
                if (action == LocalModAction.Install
                    || action == LocalModAction.Uninstall
                    || mod.LatestVersion?.Major != mod.Version.Major // *1*.2.0
                    || mod.LatestVersion?.Minor != mod.Version.Minor) // 1.*2*.0
                {
                    return new Version(modpackVersion.Major, modpackVersion.Minor + 1, 0);
                }
            }

            return new Version(modpackVersion.Major, modpackVersion.Minor, modpackVersion.Build + 1);
        }
    }
}