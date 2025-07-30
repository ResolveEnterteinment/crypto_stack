using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Models.KYC
{
    /// <summary>
    /// Bounding box for OCR field location
    /// </summary>
    public class BoundingBox
    {
        [BsonElement("x")]
        public int X { get; set; }

        [BsonElement("y")]
        public int Y { get; set; }

        [BsonElement("width")]
        public int Width { get; set; }

        [BsonElement("height")]
        public int Height { get; set; }
    }
}
