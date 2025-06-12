using Domain.DTOs.KYC.OCR;
using Newtonsoft.Json;

namespace crypto_investment_project.Server.Services.OCR
{
    public class IdCardValidationService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<IdCardValidationService> _logger;
        private readonly string _ocrApiKey;
        private readonly string _ocrEndpoint;

        public IdCardValidationService(
            HttpClient httpClient,
            ILogger<IdCardValidationService> logger,
            IConfiguration configuration)
        {
            _httpClient = httpClient;
            _logger = logger;
            _ocrApiKey = configuration["OCR:ApiKey"];
            _ocrEndpoint = configuration["OCR:Endpoint"];
        }

        public async Task<IdCardValidationResult> ValidateIdCard(string base64Image)
        {
            try
            {
                // Remove data:image/jpeg;base64, prefix if present
                string base64Data = base64Image;
                if (base64Data.Contains(","))
                {
                    base64Data = base64Data.Split(',')[1];
                }

                // Option 1: Use a commercial OCR API (like Microsoft Azure Computer Vision)
                IdCardValidationResult result = await ValidateWithAzure(base64Data);

                // Option 2: Use a local OCR engine like Tesseract.NET if needed
                // var result = await ValidateWithLocalEngine(base64Data);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating ID card");
                return new IdCardValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Failed to validate ID card"
                };
            }
        }

        private async Task<IdCardValidationResult> ValidateWithAzure(string base64Image)
        {
            try
            {
                // Prepare API request
                StringContent content = new(JsonConvert.SerializeObject(new
                {
                    image = base64Image
                }), System.Text.Encoding.UTF8, "application/json");

                // Add API key as header
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", _ocrApiKey);

                // Make the request to Azure OCR service
                HttpResponseMessage response = await _httpClient.PostAsync($"{_ocrEndpoint}/vision/v3.2/read/analyze", content);
                _ = response.EnsureSuccessStatusCode();

                // Get operation ID from response headers
                string? operationLocation = response.Headers.GetValues("Operation-Location").FirstOrDefault();
                string operationId = operationLocation.Split('/').Last();

                // Poll for results
                return await PollForResults(operationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Azure OCR processing");
                throw;
            }
        }

        private async Task<IdCardValidationResult> PollForResults(string operationId)
        {
            int maxRetries = 10;
            int delay = 1000; // 1 second initial delay

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    // Add API key as header
                    _httpClient.DefaultRequestHeaders.Clear();
                    _httpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", _ocrApiKey);

                    // Poll for results
                    HttpResponseMessage response = await _httpClient.GetAsync($"{_ocrEndpoint}/vision/v3.2/read/analyzeResults/{operationId}");
                    _ = response.EnsureSuccessStatusCode();

                    string responseContent = await response.Content.ReadAsStringAsync();
                    dynamic? result = JsonConvert.DeserializeObject<dynamic>(responseContent);

                    if (result.status == "succeeded")
                    {
                        // Process OCR results
                        return ProcessOcrResults(result);
                    }
                    else if (result.status == "failed")
                    {
                        return new IdCardValidationResult
                        {
                            IsValid = false,
                            ErrorMessage = "OCR processing failed"
                        };
                    }

                    // Wait before next poll
                    await Task.Delay(delay);
                    delay *= 2; // Exponential backoff
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error polling OCR results (attempt {i + 1}/{maxRetries})");
                    await Task.Delay(delay);
                    delay *= 2;
                }
            }

            return new IdCardValidationResult
            {
                IsValid = false,
                ErrorMessage = "OCR processing timed out"
            };
        }

        private IdCardValidationResult ProcessOcrResults(dynamic ocrResult)
        {
            try
            {
                List<string> extractedLines = [];

                // Extract text lines from OCR result
                if (ocrResult.analyzeResult != null && ocrResult.analyzeResult.readResults != null)
                {
                    foreach (var readResult in ocrResult.analyzeResult.readResults)
                    {
                        if (readResult.lines != null)
                        {
                            foreach (var line in readResult.lines)
                            {
                                // Use dynamic type's ToString() method instead of direct property access
                                string? lineText = line?.text?.ToString();
                                if (!string.IsNullOrEmpty(lineText))
                                {
                                    extractedLines.Add(lineText);
                                }
                            }
                        }
                    }
                }

                // Join all lines
                string fullText = string.Join(" ", extractedLines);

                // Perform validation checks
                bool hasName = ContainsPattern(fullText, @"Name:?\s*([A-Za-z\s]+)");
                bool hasDob = ContainsPattern(fullText, @"Birth(?:day|date)?:?\s*(\d{1,2}[-.\/]\d{1,2}[-.\/]\d{2,4})");
                bool hasDocNumber = ContainsPattern(fullText, @"(?:ID|Document|Card|No|Number|#):?\s*([A-Z0-9]{6,12})");

                // Simple validation - require at least name and one other field
                bool isValid = hasName && (hasDob || hasDocNumber);

                return new IdCardValidationResult
                {
                    IsValid = isValid,
                    ExtractedText = fullText,
                    ContainsName = hasName,
                    ContainsDateOfBirth = hasDob,
                    ContainsDocumentNumber = hasDocNumber,
                    ConfidenceScore = isValid ? 0.8 : 0.4
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing OCR results");
                return new IdCardValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"Failed to process OCR results: {ex.Message}"
                };
            }
        }

        private bool ContainsPattern(string text, string pattern)
        {
            return System.Text.RegularExpressions.Regex.IsMatch(text, pattern);
        }
    }
}