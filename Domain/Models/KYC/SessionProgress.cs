using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Models.KYC
{
    /// <summary>
    /// Session progress tracking
    /// </summary>
    public class SessionProgress
    {
        [BsonElement("personalInfoCompleted")]
        public bool PersonalInfoCompleted { get; set; }

        [BsonElement("documentsUploaded")]
        public bool DocumentsUploaded { get; set; }

        [BsonElement("biometricCompleted")]
        public bool BiometricCompleted { get; set; }

        [BsonElement("termsAccepted")]
        public bool TermsAccepted { get; set; }

        [BsonElement("consentGiven")]
        public bool ConsentGiven { get; set; }

        [BsonElement("currentStep")]
        public int CurrentStep { get; set; }

        [BsonElement("totalSteps")]
        public int TotalSteps { get; set; } = 4;

        [BsonElement("completionPercentage")]
        public double CompletionPercentage { get; set; }
    }
}
