using System.Collections.Generic;

namespace OoLunar.ConvenientCompany.Tools.SemVerParser.Entities
{
    public sealed class LocalModIdEqualityComparer : IEqualityComparer<LocalMod>
    {
        public static readonly LocalModIdEqualityComparer Instance = new();

        public bool Equals(LocalMod? x, LocalMod? y) => x?.Author == y?.Author && x?.ModName == y?.ModName;
        public int GetHashCode(LocalMod obj) => obj.Author.GetHashCode() ^ obj.ModName.GetHashCode();
    }
}