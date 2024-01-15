using System.Text.Json;
using LibGit2Sharp;
using OoLunar.ConvenientCompany.Tools.SemVerParser.Entities.Api;

namespace OoLunar.ConvenientCompany.Tools.SemVerParser
{
    public sealed class GitTools
    {
        public static readonly Repository _repository = new(ThisAssembly.Project.ProjectRoot + "/.git");

        public static ThunderStoreManifest? GetLastPublishedManifest()
        {
            // Compare latest commit to the latest tag.
            Commit? latestTag = null;
            Commit latestCommit = _repository.Head.Tip;
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
                .Deserialize<ThunderStoreManifest>(((Blob)latestTag["manifest.json"].Target).GetContentText(), Program.JsonSerializerDefaults)
                .NullPanic($"Unable to parse manifest file on tag {latestTag.Id}.");
        }
    }
}