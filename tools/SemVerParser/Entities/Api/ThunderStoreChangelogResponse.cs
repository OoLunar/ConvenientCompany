namespace OoLunar.ConvenientCompany.Tools.SemVerParser.Entities.Api
{
    public sealed record ThunderStoreChangelogResponse
    {
        public string? Detail { get; init; }
        public string? Markdown { get; init; }
    }
}