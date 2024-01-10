using System;

namespace OoLunar.ConvenientCompany.Tools.SemVerParser
{
    public sealed record ThunderStoreMod
    {
        public required string Author { get; init; }
        public required string ModName { get; init; }
        public required Version Version { get; init; }
    }
}