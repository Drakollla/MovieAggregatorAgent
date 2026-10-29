using Domain;

namespace Contracts
{
    public interface IMovieService
    {
        Task<IReadOnlyList<Movie>> SearchMoviesAsync(
            string query,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<Movie>> SearchByCriteriaAsync(
            MovieSearchCriteria criteria,
            CancellationToken cancellationToken = default);

        Task<Movie?> GetMovieAsync(
            int id,
            CancellationToken cancellationToken = default);
    }
}
