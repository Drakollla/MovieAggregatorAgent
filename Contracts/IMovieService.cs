namespace Contracts
{
    public interface IMovieService
    {
        Task<string> SearchMovieAsync(string query);

        Task<string> SearchByCriteriaAsync(string? genre = null,
            int? yearFrom = null, 
            int? yearTo = null,
            float? ratingMin = null, 
            string? keyword = null);

        Task<string> GetMovieByIdAsync(int id);
    }
}