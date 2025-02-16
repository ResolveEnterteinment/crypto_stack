using MongoDB.Bson;

namespace crypto_investment_project.Server.Helpers
{

    public static class ObjectIdExtensions
    {
        /// <summary>
        /// Converts a MongoDB ObjectId to a Guid by padding the 12 bytes of the ObjectId with 4 zeros.
        /// </summary>
        public static Guid ToGuid(this ObjectId objectId)
        {
            // Convert ObjectId to its 12-byte array representation
            var objectIdBytes = objectId.ToByteArray();

            // Create a new 16-byte array for the Guid
            var guidBytes = new byte[16];

            // Copy the 12 bytes of the ObjectId into the Guid byte array.
            // The remaining 4 bytes will be zeros.
            Array.Copy(objectIdBytes, guidBytes, objectIdBytes.Length);

            return new Guid(guidBytes);
        }

        /// <summary>
        /// (Optional) Converts a Guid (created using ToGuid) back to an ObjectId.
        /// This assumes the Guid was originally generated from an ObjectId.
        /// </summary>
        public static ObjectId ToObjectId(this Guid guid)
        {
            var guidBytes = guid.ToByteArray();
            var objectIdBytes = new byte[12];
            Array.Copy(guidBytes, objectIdBytes, objectIdBytes.Length);
            return new ObjectId(objectIdBytes);
        }
    }
}