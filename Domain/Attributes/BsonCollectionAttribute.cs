namespace Domain.Attributes
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class BsonCollectionAttribute : Attribute
    {
        public string CollectionName { get; }
        public BsonCollectionAttribute(string collectionName) => CollectionName = collectionName;
    }

}
