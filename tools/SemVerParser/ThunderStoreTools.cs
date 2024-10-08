using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Compression;
using System.IO.Pipelines;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OoLunar.ConvenientCompany.Tools.SemVerParser.Entities;
using OoLunar.ConvenientCompany.Tools.SemVerParser.Entities.Api;

namespace OoLunar.ConvenientCompany.Tools.SemVerParser
{
    public static class ThunderStoreTools
    {
        private sealed class ThunderStoreRateLimitHandler : DelegatingHandler
        {
            public ThunderStoreRateLimitHandler() : base(new HttpClientHandler()) { }
            public ThunderStoreRateLimitHandler(HttpMessageHandler innerHandler) : base(innerHandler) { }
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                HttpResponseMessage responseMessage;
                do
                {
                    responseMessage = await base.SendAsync(request, cancellationToken);
                    if (responseMessage.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        Console.WriteLine("Hit the ratelimit. Waiting 15 seconds...");
                        await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
                    }
                } while (responseMessage.StatusCode == HttpStatusCode.TooManyRequests);

                return responseMessage;
            }
        }

        private static readonly HttpClient _httpClient = new(new ThunderStoreRateLimitHandler())
        {
            DefaultRequestHeaders =
            {
                { "User-Agent", Program.HTTP_AGENT }
            }
        };

        public static IReadOnlyDictionary<LocalMod, LocalModAction> GetLocalModDiff(ThunderStoreManifest currentManifest, ThunderStoreManifest? lastManifest)
        {
            Dictionary<LocalMod, LocalModAction> modStatuses = [];
            if (lastManifest is null)
            {
                foreach (LocalMod localMod in currentManifest.Dependencies)
                {
                    modStatuses.Add(localMod, LocalModAction.Install);
                }

                return modStatuses;
            }

            for (int i = 0; i < currentManifest.Dependencies.Count; i++)
            {
                LocalMod newMod = currentManifest.Dependencies[i];

                // Declaring j out of scope to use later.
                int j;
                for (j = 0; j < lastManifest.Dependencies.Count; j++)
                {
                    LocalMod oldMod = lastManifest.Dependencies[j];

                    // Ensure that the newMod and the oldMod have the same mod id.
                    if (newMod.Author != oldMod.Author || newMod.ModName != oldMod.ModName)
                    {
                        continue;
                    }

                    // Check if the mod was updated.
                    if (newMod.TrueVersion > oldMod.TrueVersion)
                    {
                        newMod.LatestVersion = newMod.TrueVersion;
                        newMod.VersionNumber = oldMod.VersionNumber;
                        modStatuses.Add(newMod, LocalModAction.Upgrade);
                    }
                    // Check if the mod was downgraded.
                    else if (newMod.TrueVersion < oldMod.TrueVersion)
                    {
                        newMod.LatestVersion = oldMod.TrueVersion;
                        modStatuses.Add(newMod, LocalModAction.Downgrade);
                    }
                    // Otherwise, the mod was not updated locally.
                    else
                    {
                        modStatuses.Add(newMod, LocalModAction.None);
                    }

                    // Exit the inner loop once the mod is found.
                    break;
                }

                // If j is equal to the last index of the lastManifest, the mod was not found in lastManifest.
                if (j == lastManifest.Dependencies.Count)
                {
                    modStatuses.Add(newMod, LocalModAction.Install);
                }
            }

            // Check for removed mods
            for (int i = 0; i < lastManifest.Dependencies.Count; i++)
            {
                LocalMod oldMod = lastManifest.Dependencies[i];

                // Declaring j out of scope to use later.
                int j;
                for (j = 0; j < currentManifest.Dependencies.Count; j++)
                {
                    LocalMod newMod = currentManifest.Dependencies[j];

                    // Ensure that the newMod and the oldMod have the same mod id.
                    if (newMod.Author != oldMod.Author || newMod.ModName != oldMod.ModName)
                    {
                        continue;
                    }

                    // Exit the inner loop once the mod is found.
                    break;
                }

                // If j is equal to the last index of the currentManifest, the mod was not found in currentManifest.
                if (j == currentManifest.Dependencies.Count)
                {
                    modStatuses.Add(oldMod, LocalModAction.Uninstall);
                }
            }

            return modStatuses;
        }

        public static async ValueTask<IReadOnlyDictionary<LocalMod, LocalModAction>> CheckForRemoteUpdatesAsync(IReadOnlyDictionary<LocalMod, LocalModAction> localModDiff)
        {
            Dictionary<LocalMod, LocalModAction> remoteUpdates = new(localModDiff, LocalModIdEqualityComparer.Instance);
            HttpResponseMessage responseMessage = await _httpClient.GetAsync("https://thunderstore.io/api/experimental/package-index/");
            responseMessage.EnsureSuccessStatusCode();

            PipeReader reader = PipeReader.Create(new GZipStream(await responseMessage.Content.ReadAsStreamAsync(), CompressionMode.Decompress, false));
            ReadResult result;
            do
            {
                result = await reader.ReadAsync();
                ReadOnlySequence<byte> buffer = result.Buffer;
                long bytesConsumed;
                do
                {
                    bytesConsumed = ParseJsonSegment(buffer, remoteUpdates);
                    buffer = buffer.Slice(bytesConsumed);
                    reader.AdvanceTo(buffer.Start, buffer.End);
                } while (bytesConsumed != 0 && !buffer.IsEmpty);
            } while (!result.IsCompleted && !result.IsCanceled);

            static long ParseJsonSegment(ReadOnlySequence<byte> buffer, Dictionary<LocalMod, LocalModAction> localModMap)
            {
                SequenceReader<byte> sequenceReader = new(buffer);
                if (!sequenceReader.TryAdvanceTo((byte)'\n', true))
                {
                    return 0;
                }

                Utf8JsonReader jsonReader = new(sequenceReader.Sequence);
                ThunderStorePackageListing? remoteModListing = JsonSerializer.Deserialize<ThunderStorePackageListing>(ref jsonReader, Program.JsonSerializerDefaults);
                if (remoteModListing is null)
                {
                    return jsonReader.BytesConsumed;
                }

                LocalMod remoteMod = new()
                {
                    Author = remoteModListing.Namespace,
                    ModName = remoteModListing.Name,
                    VersionNumber = remoteModListing.VersionNumber
                };

                foreach (LocalMod localMod in localModMap.Keys)
                {
                    if (LocalModIdEqualityComparer.Instance.Equals(localMod, remoteMod) && localMod.TrueVersion < remoteModListing.TrueVersionNumber)
                    {
                        localMod.LatestVersion = remoteModListing.TrueVersionNumber;
                        LocalModAction localModAction = localModMap[localMod];
                        if (localModAction is not LocalModAction.Install)
                        {
                            localModMap[localMod] = LocalModAction.Upgrade;
                        }

                        break;
                    }
                }

                return jsonReader.BytesConsumed + 1;
            }

            return remoteUpdates;
        }

        public static async ValueTask<Uri?> GetWebsiteUrlAsync(LocalMod localMod)
        {
            HttpResponseMessage responseMessage = await _httpClient.GetAsync($"https://thunderstore.io/api/experimental/package/{localMod.Author}/{localMod.ModName}/");
            responseMessage.EnsureSuccessStatusCode();

            ThunderStorePackage? remoteMod = await responseMessage.Content.ReadFromJsonAsync<ThunderStorePackage>(Program.JsonSerializerDefaults);
            return string.IsNullOrWhiteSpace(remoteMod?.Latest.WebsiteUrl) ? null : new(remoteMod.Latest.WebsiteUrl);
        }

        public static async ValueTask<ThunderStoreChangelogOrReadMeResponse?> GetChangelogAsync(LocalMod localMod)
        {
            HttpResponseMessage responseMessage = await _httpClient.GetAsync($"https://thunderstore.io/api/experimental/package/{localMod.Author}/{localMod.ModName}/{localMod.LatestVersion ?? localMod.TrueVersion}/changelog/");
            return await responseMessage.Content.ReadFromJsonAsync<ThunderStoreChangelogOrReadMeResponse>(Program.JsonSerializerDefaults);
        }

        public static async ValueTask<ThunderStoreChangelogOrReadMeResponse?> GetReadMeAsync(LocalMod localMod)
        {
            HttpResponseMessage responseMessage = await _httpClient.GetAsync($"https://thunderstore.io/api/experimental/package/{localMod.Author}/{localMod.ModName}/{localMod.LatestVersion ?? localMod.TrueVersion}/readme/");
            return await responseMessage.Content.ReadFromJsonAsync<ThunderStoreChangelogOrReadMeResponse>(Program.JsonSerializerDefaults);
        }
    }
}