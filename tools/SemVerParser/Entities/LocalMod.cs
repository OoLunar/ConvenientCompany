using System;
using System.Text.Json.Serialization;

namespace OoLunar.ConvenientCompany.Tools.SemVerParser.Entities
{
    [JsonConverter(typeof(LocalModJsonConverter))]
    public sealed record LocalMod
    {
        public required string Author { get; init; }
        public required string ModName { get; init; }
        public required Version Version { get; set; }

        [JsonIgnore] // This property should only be kept in memory.
        public Version? LatestVersion { get; set; }
    }
}