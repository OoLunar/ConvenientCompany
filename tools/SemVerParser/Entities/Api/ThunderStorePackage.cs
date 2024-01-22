using System;
using System.Collections.Generic;

namespace OoLunar.ConvenientCompany.Tools.SemVerParser.Entities.Api
{
    public sealed record ThunderStorePackage
    {
        public required string Namespace { get; init; }
        public required string Name { get; init; }
        public required string FullName { get; init; }
        public required string Owner { get; init; }
        public required string PackageUrl { get; init; }
        public required DateTimeOffset DateCreated { get; init; }
        public required DateTimeOffset DateUpdated { get; init; }
        public required double RatingScore { get; init; }
        public required bool IsPinned { get; init; }
        public required bool IsDeprecated { get; init; }
        public required int TotalDownloads { get; init; }
        public required ThunderStorePackageLatestVersion Latest { get; init; }
        public required IReadOnlyList<ThunderStorePackageCommunityListing> CommunityListings { get; init; }
    }
}