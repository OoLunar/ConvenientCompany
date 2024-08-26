using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OoLunar.ConvenientCompany.Tools.SemVerParser.Entities.Api
{
    public sealed record ThunderStorePackageListing
    {
        public required string Namespace { get; init; }
        public required string Name { get; init; }
        public required string VersionNumber { get; init; }
        public required IReadOnlyList<LocalMod> Dependencies { get; init; }

        [JsonIgnore]
        public Version? TrueVersionNumber => Version.TryParse(VersionNumber, out Version? version) ? version : null;
    }
}