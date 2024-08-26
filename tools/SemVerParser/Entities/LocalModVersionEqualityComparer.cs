using System.Collections.Generic;

namespace OoLunar.ConvenientCompany.Tools.SemVerParser.Entities
{
    public sealed class LocalModVersionEqualityComparer : IEqualityComparer<LocalMod>
    {
        public static readonly LocalModVersionEqualityComparer Instance = new();

        public bool Equals(LocalMod? x, LocalMod? y) => x?.Author == y?.Author && x?.ModName == y?.ModName && (x?.LatestVersion ?? x?.TrueVersion) == y?.TrueVersion;
        public int GetHashCode(LocalMod obj) => obj.Author.GetHashCode() ^ obj.ModName.GetHashCode() ^ (obj.LatestVersion?.GetHashCode() ?? obj.TrueVersion.GetHashCode());
    }
}