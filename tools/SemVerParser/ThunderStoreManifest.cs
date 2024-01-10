using System;
using System.Collections.Generic;

namespace OoLunar.ConvenientCompany.Tools.SemVerParser
{
    public sealed record ThunderStoreManifest
    {
        public required string Name { get; init; }
        public required Version VersionNumber { get; init; }
        public required Uri WebsiteUrl { get; init; }
        public required string Description { get; init; }
        public required IReadOnlyList<string> Dependencies { get; init; }
    }
}