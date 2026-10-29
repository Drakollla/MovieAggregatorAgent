namespace Domain
{
    public sealed record Movie(
        int Id,
        string Title,
        int Year,
        float Rating,
        string? Description,
        string? PosterUrl,
        int? DurationMinutes,
        IReadOnlyList<string> Genres);
}
