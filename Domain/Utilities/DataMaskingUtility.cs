using System.Text;
using System.Text.RegularExpressions;

namespace Domain.Utilities
{
    /// <summary>
    /// Utility class for masking sensitive information in logs, responses, and data display
    /// </summary>
    public static class DataMaskingUtility
    {
        private static readonly Regex EmailRegex = new(@"^([^@]+)@(.+)$", RegexOptions.Compiled);
        private static readonly Regex PhoneRegex = new(@"^(\+?[\d\s\-\(\)]{10,15})$", RegexOptions.Compiled);
        private static readonly Regex CreditCardRegex = new(@"^(\d{4})(\d{4,8})(\d{4})$", RegexOptions.Compiled);
        private static readonly Regex AlphanumericRegex = new(@"^([a-zA-Z0-9]{3,})$", RegexOptions.Compiled);

        /// <summary>
        /// Masks an email address (e.g., john.doe@example.com -> j***@e***.com)
        /// </summary>
        public static string MaskEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return email;

            var match = EmailRegex.Match(email.Trim());
            if (!match.Success)
                return "***@***.***"; // Invalid email format

            var localPart = match.Groups[1].Value;
            var domainPart = match.Groups[2].Value;

            var maskedLocal = MaskStringPreserveEnds(localPart, 1, 0, '*');
            var maskedDomain = MaskDomain(domainPart);

            return $"{maskedLocal}@{maskedDomain}";
        }

        /// <summary>
        /// Masks a phone number (e.g., +1234567890 -> +12***7890)
        /// </summary>
        public static string MaskPhoneNumber(string phoneNumber)
        {
            if (string.IsNullOrWhiteSpace(phoneNumber))
                return phoneNumber;

            var cleaned = Regex.Replace(phoneNumber, @"[^\d\+]", "");
            
            if (cleaned.Length < 6)
                return "***";

            return MaskStringPreserveEnds(cleaned, 2, 4, '*');
        }

        /// <summary>
        /// Masks a credit card number (e.g., 1234567812345678 -> 1234***5678)
        /// </summary>
        public static string MaskCreditCard(string creditCard)
        {
            if (string.IsNullOrWhiteSpace(creditCard))
                return creditCard;

            var cleaned = Regex.Replace(creditCard, @"[^\d]", "");
            
            if (cleaned.Length < 8)
                return "****";

            return MaskStringPreserveEnds(cleaned, 4, 4, '*');
        }

        /// <summary>
        /// Masks an alphanumeric string (e.g., ABC123DEF456 -> ABC***456)
        /// </summary>
        public static string MaskAlphanumeric(string value, int preserveStart = 3, int preserveEnd = 3)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;

            return MaskStringPreserveEnds(value, preserveStart, preserveEnd, '*');
        }

        /// <summary>
        /// Masks a document number (e.g., passport, ID card)
        /// </summary>
        public static string MaskDocumentNumber(string documentNumber)
        {
            if (string.IsNullOrWhiteSpace(documentNumber))
                return documentNumber;

            var cleaned = documentNumber.Trim().ToUpperInvariant();
            return MaskStringPreserveEnds(cleaned, 2, 2, '*');
        }

        /// <summary>
        /// Masks a full name (e.g., John Michael Doe -> J*** M*** D***)
        /// </summary>
        public static string MaskFullName(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                return fullName;

            var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var maskedParts = parts.Select(part => MaskStringPreserveEnds(part, 1, 0, '*'));
            
            return string.Join(" ", maskedParts);
        }

        /// <summary>
        /// Masks a date of birth (e.g., 1990-05-15 -> 19**-**-**)
        /// </summary>
        public static string MaskDateOfBirth(DateTime? dateOfBirth)
        {
            if (!dateOfBirth.HasValue)
                return "****-**-**";

            var year = dateOfBirth.Value.Year.ToString();
            return $"{year.Substring(0, 2)}**-**-**";
        }

        /// <summary>
        /// Masks an address (preserves first part and masks the rest)
        /// </summary>
        public static string MaskAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                return address;

            var parts = address.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return "***";

            var maskedParts = new List<string> { parts[0].Trim() }; // Keep first part
            maskedParts.AddRange(parts.Skip(1).Select(_ => "***"));

            return string.Join(", ", maskedParts);
        }

        /// <summary>
        /// Masks a JSON object by masking specific fields
        /// </summary>
        public static string MaskJsonObject(string jsonString, string[] sensitiveFields)
        {
            if (string.IsNullOrWhiteSpace(jsonString))
                return jsonString;

            var result = jsonString;
            foreach (var field in sensitiveFields)
            {
                // Simple regex-based masking for JSON fields
                var pattern = $@"""({field})""\s*:\s*""([^""]*)""";
                result = Regex.Replace(result, pattern, match =>
                {
                    var fieldName = match.Groups[1].Value;
                    var fieldValue = match.Groups[2].Value;
                    var maskedValue = MaskGenericSensitiveData(fieldValue);
                    return $@"""{fieldName}"":""{maskedValue}""";
                }, RegexOptions.IgnoreCase);
            }
            return result;
        }

        /// <summary>
        /// Automatically detects and masks common sensitive data patterns
        /// </summary>
        public static string MaskGenericSensitiveData(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;

            var trimmed = value.Trim();

            // Email pattern
            if (EmailRegex.IsMatch(trimmed))
                return MaskEmail(trimmed);

            // Phone pattern
            if (PhoneRegex.IsMatch(trimmed))
                return MaskPhoneNumber(trimmed);

            // Credit card pattern (16 digits)
            var digitsOnly = Regex.Replace(trimmed, @"[^\d]", "");
            if (digitsOnly.Length >= 13 && digitsOnly.Length <= 19)
                return MaskCreditCard(trimmed);

            // Default alphanumeric masking
            if (trimmed.Length > 6)
                return MaskAlphanumeric(trimmed);

            return "***";
        }

        /// <summary>
        /// Core method to mask a string while preserving start and end characters
        /// </summary>
        private static string MaskStringPreserveEnds(string value, int preserveStart, int preserveEnd, char maskChar = '*')
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;

            var length = value.Length;

            // If string is too short, just mask it completely
            if (length <= preserveStart + preserveEnd)
            {
                return new string(maskChar, Math.Min(length, 3));
            }

            var start = value.Substring(0, preserveStart);
            var end = preserveEnd > 0 ? value.Substring(length - preserveEnd) : "";
            var middleLength = length - preserveStart - preserveEnd;
            var middle = new string(maskChar, Math.Max(middleLength, 3));

            return start + middle + end;
        }

        /// <summary>
        /// Masks domain part of email address
        /// </summary>
        private static string MaskDomain(string domain)
        {
            var parts = domain.Split('.');
            if (parts.Length < 2)
                return "***";

            var maskedParts = new List<string>();
            
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (i == parts.Length - 1) // TLD
                {
                    maskedParts.Add(part); // Keep TLD visible
                }
                else
                {
                    maskedParts.Add(MaskStringPreserveEnds(part, 1, 0, '*'));
                }
            }

            return string.Join(".", maskedParts);
        }

        /// <summary>
        /// Extension method for easy masking of object properties
        /// </summary>
        public static T MaskSensitiveProperties<T>(this T obj, params string[] propertyNames) where T : class
        {
            if (obj == null) return obj;

            var type = typeof(T);
            foreach (var propertyName in propertyNames)
            {
                var property = type.GetProperty(propertyName);
                if (property != null && property.CanWrite && property.PropertyType == typeof(string))
                {
                    var currentValue = property.GetValue(obj) as string;
                    if (!string.IsNullOrEmpty(currentValue))
                    {
                        var maskedValue = MaskGenericSensitiveData(currentValue);
                        property.SetValue(obj, maskedValue);
                    }
                }
            }

            return obj;
        }
    }
}