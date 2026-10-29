namespace Domain
{
    public sealed record MovieSearchCriteria
    {
        public IReadOnlyList<string> Genres { get; init; } = [];
        public int? YearFrom { get; init; }
        public int? YearTo { get; init; }
        public float? RatingMin { get; init; }
        public string? Keyword { get; init; }
    }
}
