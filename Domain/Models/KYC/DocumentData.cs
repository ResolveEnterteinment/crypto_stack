using Domain.Attributes;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Models.KYC
{
    /// <summary>
    /// Document information for uploaded KYC documents
    /// </summary>
    [BsonCollection("documents")]
    public class DocumentData: BaseEntity
    {
        public Guid UserId { get; set; }
        public Guid SessionId { get; set; }
        public string OriginalFileName { get; set; } = string.Empty;
        public string SecureFileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string ContentType { get; set; } = string.Empty;
        public string DocumentType { get; set; } = string.Empty;
        public string FileHash { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool IsEncrypted { get; set; } = false;
        public string EncryptionMethod { get; set; } = string.Empty;
    }
}
