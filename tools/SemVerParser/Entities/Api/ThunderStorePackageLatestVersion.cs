using System;
using System.Collections.Generic;

namespace OoLunar.ConvenientCompany.Tools.SemVerParser.Entities.Api
{
    public sealed record ThunderStorePackageLatestVersion
    {
        public required string Namespace { get; init; }
        public required string Name { get; init; }
        public required string VersionNumber { get; init; }
        public required string FullName { get; init; }
        public required string Description { get; init; }
        public required string Icon { get; init; }
        public required IReadOnlyList<LocalMod> Dependencies { get; init; }
        public required string DownloadUrl { get; init; }
        public required int Downloads { get; init; }
        public required DateTimeOffset DateCreated { get; init; }
        public required string WebsiteUrl { get; init; }
        public required bool IsActive { get; init; }
    }
}