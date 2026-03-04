namespace Domain
{
    public class Movie
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public int Year { get; set; }
        public string ImdbId { get; set; }
        public float Rating {  get; set; }
        public List<MovieLink> Links { get; set; } = [];
    }
}