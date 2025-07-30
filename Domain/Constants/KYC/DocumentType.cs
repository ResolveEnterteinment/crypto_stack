public static class DocumentType
{
    public const string Passport = "passport";
    public const string DriversLicense = "drivers_license";
    public const string NationalId = "national_id";
    public const string UtilityBill = "utility_bill";
    public const string BankStatement = "bank_statement";
    public const string TaxDocument = "tax_document";
    public const string IncomeProof = "income_proof";

    public static readonly string[] IdentityDocuments = {
        Passport, DriversLicense, NationalId
    };

    public static readonly string[] AddressDocuments = {
        UtilityBill, BankStatement
    };

    public static readonly string[] FinancialDocuments = {
        BankStatement, TaxDocument, IncomeProof
    };

    // Documents that require live capture
    public static readonly string[] LiveCaptureRequired = {
        Passport, DriversLicense, NationalId
    };

    // Documents that require duplex capture (front and back)
    public static readonly string[] DuplexCaptureRequired = {
        DriversLicense, NationalId
    };

    // Documents that can be uploaded (utility bills, etc.)
    public static readonly string[] UploadAllowed = {
        UtilityBill, BankStatement, TaxDocument, IncomeProof
    };

    public static bool RequiresLiveCapture(string documentType)
    {
        return LiveCaptureRequired.Contains(documentType);
    }

    public static bool RequiresDuplexCapture(string documentType)
    {
        return DuplexCaptureRequired.Contains(documentType);
    }

    public static bool AllowsUpload(string documentType)
    {
        return UploadAllowed.Contains(documentType);
    }
}