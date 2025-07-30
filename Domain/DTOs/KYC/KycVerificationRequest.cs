using MongoDB.Bson.Serialization.Attributes;
using Newtonsoft.Json.Linq;

namespace Domain.DTOs.KYC
{
    public class KycVerificationRequest
    {
        public Guid UserId { get; set; }
        public Guid SessionId { get; set; }
        public string VerificationLevel { get; set; } = "BASIC";
        public required Dictionary<string, object> Data { get; set; }
        public required bool ConsentGiven { get; set; } = false;
        public required bool TermsAccepted { get; set; } = false;
    }

    [BsonIgnoreExtraElements]
    public class AddressRequest
    {
        [BsonElement("street")]
        public required string Street { get; set; } = string.Empty;

        [BsonElement("city")]
        public required string City { get; set; } = string.Empty;

        [BsonElement("state")]
        public required string State { get; set; } = string.Empty;

        [BsonElement("zipCode")]
        public required string ZipCode { get; set; } = string.Empty;

        [BsonElement("country")]
        public required string Country { get; set; } = string.Empty;
    }

    public class BasicPersonalInfoRequest
    {
        public required string FullName { get; set; } = string.Empty;
        public required string DateOfBirth { get; set; } = string.Empty;
        public required AddressRequest Address { get; set; }
    }

    public class BasicKycDataRequest
    {
        public required BasicPersonalInfoRequest PersonalInfo { get; set; }
        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
            {
                { "PersonalInfo", new Dictionary<string, object>
                    {
                        { "FullName", PersonalInfo.FullName },
                        { "DateOfBirth", PersonalInfo.DateOfBirth },
                        { "Address", new Dictionary<string, string>
                            {
                                { "Street", PersonalInfo.Address.Street },
                                { "City", PersonalInfo.Address.City },
                                { "State", PersonalInfo.Address.State },
                                { "ZipCode", PersonalInfo.Address.ZipCode },
                                { "Country", PersonalInfo.Address.Country }
                            }
                        }
                    }
                }
            };
        }
        public static BasicKycDataRequest FromDictionary(Dictionary<string, object> data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var personalInfo = data.TryGetValue("personalInfo", out var personalInfoObj)
                ? JObject.FromObject(personalInfoObj).ToObject<Dictionary<string, object>>()
                : data;
            var address = personalInfo.TryGetValue("address", out var addressObj)
                ? JObject.FromObject(addressObj).ToObject<Dictionary<string, object>>()
                : personalInfo;
            return new BasicKycDataRequest
            {
                PersonalInfo = new BasicPersonalInfoRequest
                {
                    FullName = personalInfo["fullName"] as string ?? string.Empty,
                    DateOfBirth = personalInfo["dateOfBirth"] as string ?? string.Empty,
                    Address = new AddressRequest
                    {
                        Street = (address as Dictionary<string, object>)?["street"] as string ?? string.Empty,
                        City = (personalInfo["address"] as Dictionary<string, object>)?["city"] as string ?? string.Empty,
                        State = (personalInfo["address"] as Dictionary<string, object>)?["state"] as string ?? string.Empty,
                        ZipCode = (personalInfo["address"] as Dictionary<string, object>)?["zipCode"] as string ?? string.Empty,
                        Country = (personalInfo["address"] as Dictionary<string, object>)?["country"] as string ?? string.Empty
                    }
                }
            };
        }
    }

    public class StandardPersonalInfoRequest
    {
        public required string Nationality { get; set; } = string.Empty;
        public required string GovernmentIdNumber { get; set; } = string.Empty;
        public required string PhoneNumber { get; set; }
        public required string Occupation { get; set; }
    }
    public class StandardKycDataRequest
    {
        public required StandardPersonalInfoRequest PersonalInfo { get; set; }
        public required IEnumerable<KycDocument> Documents { get; set; } = new List<KycDocument>();
        public required string? SelfieHash { get; set; } = null;

        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
            {
                { "PersonalInfo", new Dictionary<string, string>
                    {
                        { "Nationality", PersonalInfo.Nationality },
                        { "GovernmentIdNumber", PersonalInfo.GovernmentIdNumber },
                        { "PhoneNumber", PersonalInfo.PhoneNumber },
                        { "Occupation", PersonalInfo.Occupation }
                    }
                },
                { "Documents", Documents.Select(doc => new Dictionary<string, object>
                    {
                        { "Id", doc.Id },
                        { "Type", doc.Type },
                        { "FileHashes", doc.FileHashes },
                        { "UploadDate", doc.UploadDate },
                        { "IsLiveCapture", doc.IsLiveCapture }
                    }).ToList()
                },
                { "SelfieHash", SelfieHash }
            };
        }
        public static StandardKycDataRequest FromDictionary(Dictionary<string, object> data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var personalInfo = data.TryGetValue("personalInfo", out var personalInfoObj)
                ? JObject.FromObject(personalInfoObj).ToObject<Dictionary<string, object>>()
                : data;
            return new StandardKycDataRequest
            {
                PersonalInfo = new StandardPersonalInfoRequest
                {
                    Nationality = personalInfo["nationality"] as string ?? string.Empty,
                    GovernmentIdNumber = personalInfo["governmentIdNumber"] as string ?? string.Empty,
                    PhoneNumber = personalInfo["phoneNumber"] as string ?? string.Empty,
                    Occupation = personalInfo["occupation"] as string ?? string.Empty
                },
                Documents = ((IEnumerable<object>)data["documents"]).Select(docData => new KycDocument
                {
                    Id = Guid.Parse(docData.GetType().GetProperty("id")?.GetValue(docData)?.ToString() ?? Guid.Empty.ToString()),
                    Type = docData.GetType().GetProperty("type")?.GetValue(docData)?.ToString() ?? string.Empty,
                    FileHashes = (docData.GetType().GetProperty("hashe")?.GetValue(docData) as IEnumerable<string>)?.ToArray() ?? Array.Empty<string>(),
                    UploadDate = DateTime.Parse(docData.GetType().GetProperty("uploadDate")?.GetValue(docData)?.ToString() ?? DateTime.MinValue.ToString()),
                    IsLiveCapture = (bool)(docData.GetType().GetProperty("isLiveCapture")?.GetValue(docData) ?? false)
                }).ToList(),
                SelfieHash = data.ContainsKey("selfieHash") ? data["selfieHash"] as string : null
            };
        }
    }
}