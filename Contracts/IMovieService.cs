namespace Contracts
{
    public interface IMovieService
    {
        Task<string> SearchMovieAsync(string query);
    }
}