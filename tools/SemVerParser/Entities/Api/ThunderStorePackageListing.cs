using System;
using System.Collections.Generic;

namespace OoLunar.ConvenientCompany.Tools.SemVerParser.Entities.Api
{
    public sealed record ThunderStorePackageListing
    {
        public required string Namespace { get; init; }
        public required string Name { get; init; }
        public required Version VersionNumber { get; init; }
        public required IReadOnlyList<LocalMod> Dependencies { get; init; }
    }
}