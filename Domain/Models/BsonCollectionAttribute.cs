using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Models
{
    // Attribute for MongoDB collection mapping
    namespace Domain.Models
    {
        [AttributeUsage(AttributeTargets.Class, Inherited = false)]
        public class BsonCollectionAttribute : Attribute
        {
            public string CollectionName { get; }

            public BsonCollectionAttribute(string collectionName)
            {
                CollectionName = collectionName;
            }
        }
    }
}
