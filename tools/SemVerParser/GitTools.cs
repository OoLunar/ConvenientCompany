using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;
using OoLunar.ConvenientCompany.Tools.SemVerParser.Entities.Api;
using Version = System.Version;

namespace OoLunar.ConvenientCompany.Tools.SemVerParser
{
    public sealed class GitTools
    {
        private sealed class RemoteGitRateLimitHandler : DelegatingHandler
        {
            private readonly SemaphoreSlim _rateLimitSemaphore = new(1, 1);

            private int _rateLimitRemaining;
            private DateTimeOffset _rateLimitReset = DateTimeOffset.MinValue;

            public RemoteGitRateLimitHandler() : base(new HttpClientHandler()) { }
            public RemoteGitRateLimitHandler(HttpMessageHandler innerHandler) : base(innerHandler) { }
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.RequestUri is null)
                {
                    // This should never happen.
                    throw new InvalidOperationException("Request URI is null.");
                }

                bool isGitRequest = request.RequestUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || request.RequestUri.Host.Equals("gitlab.com", StringComparison.OrdinalIgnoreCase);
                HttpResponseMessage response;
                do
                {
                    // Pre-emptively wait for ratelimits to be reset.
                    if (isGitRequest)
                    {
                        await _rateLimitSemaphore.WaitAsync(cancellationToken);
                        try
                        {
                            if (_rateLimitRemaining == 0)
                            {
                                TimeSpan waitTime = _rateLimitReset - DateTimeOffset.UtcNow;
                                Console.WriteLine($"Hit the ratelimit. Waiting until {_rateLimitReset}...");
                                await Task.Delay(waitTime, cancellationToken);
                            }
                        }
                        finally
                        {
                            _rateLimitSemaphore.Release();
                        }
                    }

                    // Make the request
                    response = await base.SendAsync(request, cancellationToken);

                    // Update ratelimits
                    if (isGitRequest)
                    {
                        if (!response.Headers.TryGetValues("x-ratelimit-reset", out IEnumerable<string>? resetValues) || !response.Headers.TryGetValues("x-ratelimit-remaining", out IEnumerable<string>? remainingValues))
                        {
                            // This should never happen.
                            throw new InvalidOperationException("Missing x-ratelimit-reset or x-ratelimit-remaining headers.");
                        }
                        else if (!int.TryParse(remainingValues.Single(), out int remaining) || !long.TryParse(resetValues.Single(), out long reset))
                        {
                            // This should never happen.
                            throw new InvalidOperationException("Unable to parse x-ratelimit-reset or x-ratelimit-remaining headers.");
                        }
                        else
                        {
                            _rateLimitRemaining = remaining;
                            _rateLimitReset = DateTimeOffset.FromUnixTimeSeconds(reset);
                        }
                    }
                } while (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests);

                return response;
            }
        }

        private static readonly Repository _repository = new(ThisAssembly.Project.ProjectRoot + "/.git");
        private static readonly HttpClient _httpClient = new(new RemoteGitRateLimitHandler())
        {
            DefaultRequestHeaders =
            {
                { "User-Agent", Program.HTTP_AGENT }
            }
        };

        public static IReadOnlyList<Tag> GetTags() => _repository.Tags.OrderBy(x => Version.Parse(x.FriendlyName)).ToList();

        public static ThunderStoreManifest? GetLastPublishedManifest(Tag? fromTag = null)
        {
            // Compare latest commit to the latest tag.
            Commit? latestTag = null;
            Commit latestCommit = fromTag?.PeeledTarget.Peel<Commit>() ?? _repository.Head.Tip;
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
                return null;
            }

            // Compare the old manifest file to the new one.
            return JsonSerializer
                .Deserialize<ThunderStoreManifest>(latestTag["manifest.json"].Target.Peel<Blob>().GetContentText(), Program.JsonSerializerDefaults)
                .NullPanic($"Unable to parse manifest file on tag {latestTag.Id}.");
        }

        public static ThunderStoreManifest? GetLastCommitManifest()
        {
            Commit latestCommit = _repository.Head.Tip;
            return JsonSerializer
                .Deserialize<ThunderStoreManifest>(latestCommit["manifest.json"].Target.Peel<Blob>().GetContentText(), Program.JsonSerializerDefaults)
                .NullPanic($"Unable to parse manifest file on commit {latestCommit.Id}.");
        }

        public static async ValueTask<(Uri, string?)> GetReleaseAsync(Uri repositoryUrl, Version version)
        {
            if (repositoryUrl.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            {
                HttpResponseMessage response = await _httpClient.GetAsync($"https://api.github.com/repos{repositoryUrl.AbsolutePath}/releases/tags/{version}", HttpCompletionOption.ResponseHeadersRead);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return (repositoryUrl, null);
                }
                else if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException($"Unable to get release URL for {repositoryUrl}.");
                }

                JsonDocument responseJson = await response.Content.ReadFromJsonAsync<JsonDocument>(Program.JsonSerializerDefaults) ?? throw new InvalidOperationException($"Unable to get release URL for {repositoryUrl}.");
                if (!responseJson.RootElement.TryGetProperty("html_url", out JsonElement htmlUrl) || htmlUrl.GetString() is not string htmlUrlString)
                {
                    return (repositoryUrl, null);
                }
                else if (!responseJson.RootElement.TryGetProperty("body", out JsonElement body) || body.GetString() is not string bodyString)
                {
                    return (new(htmlUrlString), null);
                }
                else
                {
                    return (new(htmlUrlString), bodyString);
                }
            }
            else if (repositoryUrl.Host.Equals("gitlab.com", StringComparison.OrdinalIgnoreCase))
            {
                HttpResponseMessage response = await _httpClient.GetAsync($"https://gitlab.com/api/v4/projects{repositoryUrl.AbsolutePath}/releases/{version}", HttpCompletionOption.ResponseHeadersRead);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return (repositoryUrl, null);
                }
                else if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException($"Unable to get release URL for {repositoryUrl}.");
                }

                JsonDocument responseJson = await response.Content.ReadFromJsonAsync<JsonDocument>(Program.JsonSerializerDefaults) ?? throw new InvalidOperationException($"Unable to get release URL for {repositoryUrl}.");
                if (!responseJson.RootElement.TryGetProperty("_links", out JsonElement links) || !links.TryGetProperty("self", out JsonElement self) || self.GetString() is not string selfString)
                {
                    return (repositoryUrl, null);
                }
                else if (!responseJson.RootElement.TryGetProperty("description", out JsonElement description) || description.GetString() is not string descriptionString)
                {
                    return (new(selfString), null);
                }
                else
                {
                    return (new(selfString), descriptionString);
                }
            }
            else
            {
                return (repositoryUrl, null);
            }
        }
    }
}