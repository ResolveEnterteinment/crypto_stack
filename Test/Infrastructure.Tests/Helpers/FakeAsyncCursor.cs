using MongoDB.Driver;

namespace Infrastructure.Tests.Helpers
{
    public class FakeAsyncCursor<T> : IAsyncCursor<T>
    {
        private readonly IEnumerator<T> _enumerator;

        public FakeAsyncCursor(IEnumerable<T> data)
        {
            _enumerator = data.GetEnumerator();
        }

        public IEnumerable<T> Current
        {
            get { return new List<T> { _enumerator.Current }; }
        }

        public void Dispose() => _enumerator.Dispose();

        public bool MoveNext(CancellationToken cancellationToken = default) => _enumerator.MoveNext();

        public Task<bool> MoveNextAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_enumerator.MoveNext());
        }
    }
}
