using Contracts;
using Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure
{
    public class FakeScraper : IMovieSource
    {
        public Task<List<Movie>> SearchAsync(string query)
        {
            var results = new List<Movie>
            {
                new Movie
                {
                    Title = $"{query} (Реальный фильм)",
                    Year = 2023,
                    Rating = 8.5f,
                    ImdbId = "tt1234567"
                },
                new Movie
                {
                    Title = $"{query} 2: Родолжение",
                    Year = 2026,
                    Rating = 7.2f,
                    ImdbId = "tt7654321"
                }
            };

            return Task.FromResult(results);
        }
    }
}
