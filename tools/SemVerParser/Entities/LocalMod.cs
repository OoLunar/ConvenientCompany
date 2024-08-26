using System;
using System.Text.Json.Serialization;

namespace OoLunar.ConvenientCompany.Tools.SemVerParser.Entities
{
    [JsonConverter(typeof(LocalModJsonConverter))]
    public sealed record LocalMod
    {
        public required string Author { get; init; }
        public required string ModName { get; init; }

        [JsonPropertyName("version")]
        public required string VersionNumber { get; set; }

        [JsonIgnore]
        public Version? TrueVersion => Version.TryParse(VersionNumber, out Version? version) ? version : null;

        [JsonIgnore] // This property should only be kept in memory.
        public Version? LatestVersion { get; set; }
    }
}