namespace OoLunar.ConvenientCompany.Tools.SemVerParser.Entities.Api
{
    public sealed record ThunderStoreChangelogOrReadMeResponse
    {
        public string? Detail { get; init; }
        public string? Markdown { get; init; }
    }
}