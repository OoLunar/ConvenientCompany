using System;
using System.Collections.Generic;

namespace OoLunar.ConvenientCompany.Tools.SemVerParser
{
    public sealed record ThunderStorePackageListing
    {
        public required string Namespace { get; init; }
        public required string Name { get; init; }
        public required string FullName { get; init; }
        public required string Owner { get; init; }
        public required Uri PackageUrl { get; init; }
        public required DateTimeOffset DateCreated { get; init; }
        public required DateTimeOffset DateUpdated { get; init; }
        public required int RatingScore { get; init; }
        public required bool IsPinned { get; init; }
        public required bool IsDeprecated { get; init; }
        public required int TotalDownloads { get; init; }
        public required Latest Latest { get; init; }
        public required IReadOnlyList<CommunityListing> CommunityListings { get; init; }
    }

    public sealed record Latest
    {
        public required string Namespace { get; init; }
        public required string Name { get; init; }
        public required string VersionNumber { get; init; }
        public required string FullName { get; init; }
        public required string Description { get; init; }
        public required Uri Icon { get; init; }
        public required IReadOnlyList<string> Dependencies { get; init; }
        public required Uri DownloadUrl { get; init; }
        public required int Downloads { get; init; }
        public required DateTimeOffset DateCreated { get; init; }
        public required Uri WebsiteUrl { get; init; }
        public required bool IsActive { get; init; }
    }

    public sealed record CommunityListing
    {
        public required bool HasNsfwContent { get; init; }
        public required IReadOnlyList<string> Categories { get; init; }
        public required string Community { get; init; }
        public required string ReviewStatus { get; init; }
    }
}