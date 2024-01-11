using System.Collections.Generic;

namespace OoLunar.ConvenientCompany.Tools.SemVerParser
{
    public sealed class ThunderStoreModEqualityComparer : IEqualityComparer<ThunderStoreMod>
    {
        public bool Equals(ThunderStoreMod? x, ThunderStoreMod? y) => x?.Author == y?.Author && x?.ModName == y?.ModName;
        public int GetHashCode(ThunderStoreMod obj) => obj.Author.GetHashCode() ^ obj.ModName.GetHashCode();
    }
}