using Domain;

namespace Contracts
{
    public interface IMovieSource
    {
        Task<List<Movie>> SearchAsync(string query); 
    }
}