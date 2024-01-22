using System.Collections.Generic;

namespace OoLunar.ConvenientCompany.Tools.SemVerParser.Entities.Api
{
    public sealed record ThunderStorePackageCommunityListing
    {
        public required bool HasNsfwContent { get; init; }
        public required IReadOnlyList<string> Categories { get; init; }
        public required string Community { get; init; }
        public required string ReviewStatus { get; init; }
    }
}