namespace Domain.DTOs
{
    /// <summary>
    /// Represents a paginated result set.
    /// </summary>
    /// <typeparam name="T">The type of items in the result.</typeparam>
    public class PaginatedResult<T>
    {
        /// <summary>
        /// The current page number.
        /// </summary>
        public int Page { get; set; }

        /// <summary>
        /// The number of items per page.
        /// </summary>
        public int PageSize { get; set; }

        /// <summary>
        /// The total number of items across all pages.
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// The total number of pages.
        /// </summary>
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);

        /// <summary>
        /// The items on the current page.
        /// </summary>
        public IEnumerable<T> Items { get; set; }

        /// <summary>
        /// Indicates if there is a previous page.
        /// </summary>
        public bool HasPreviousPage => Page > 1;

        /// <summary>
        /// Indicates if there is a next page.
        /// </summary>
        public bool HasNextPage => Page < TotalPages;
    }
}
