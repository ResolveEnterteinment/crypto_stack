import Tesseract, { createWorker } from 'tesseract.js';
const mrzParser = await import("mrz");

interface IdCardData {
  firstName: string;
  lastName: string;
  dateOfBirth: string;
  documentNumber?: string;
  expiryDate?: string;
  nationality?: string;
  issuingCountry?: string;
  gender?: string;
  mrzData?: any;
  rawText?: string;
}

export class IdCardParser {
  private static readonly LANGUAGE_MODEL = 'tur';
  
  /**
   * Main parsing function that orchestrates different parsing methods
   */
  public static async parseIdCard(imageData: string): Promise<IdCardData> {
    try {
      // Step 1: Extract all text from the ID card
      const extractedText = await this.performOcr(imageData);
      
      // Step 2: Try to identify if this is an MRZ-containing document (passport, some IDs)
      const mrzData = this.attemptMrzParsing(extractedText);
      
      // Step 3: Try different parsing strategies and combine results
      const basicParsingResults = this.parseBasicFields(extractedText);
      const patternResults = this.parseWithPatterns(extractedText);
      
      // Step 4: Combine all results, with MRZ taking precedence if available
      const combinedResults = this.combineResults(
        basicParsingResults, 
        patternResults, 
        mrzData ? this.formatMrzData(mrzData) : null
      );
      
      // Include raw text for debugging and auditing
      return {
        ...combinedResults,
        rawText: extractedText,
        mrzData
      };
    } catch (error) {
      console.error('ID card parsing error:', error);
      throw new Error('Failed to process ID card');
    }
  }
  
  /**
   * Perform OCR on the image using Tesseract
   */
  private static async performOcr(imageData: string): Promise<string> {
    try {
        const worker = await createWorker(this.LANGUAGE_MODEL, Tesseract.OEM.DEFAULT, {
            // Improve OCR quality with these options
            workerPath: 'https://unpkg.com/tesseract.js@v2.1.0/dist/worker.min.js',
            langPath: 'https://tessdata.projectnaptha.com/4.0.0',
            corePath: 'https://unpkg.com/tesseract.js-core@v2.1.0/tesseract-core.wasm.js',
            logger: m => console.debug(`Tesseract: ${m.status} ${Math.floor(m.progress * 100)}%`),
        });

      // Set image processing parameters for ID cards
      await worker.setParameters({
        tessedit_char_whitelist: 'ABCÇDEFGĞHIİJKLMNOÖPQRSŞTUÜVWXYZ0123456789abcçdefgğhıijklmnoöpqrsştuüvwxyz<>.: -/',
          tessedit_pageseg_mode: Tesseract.PSM.SINGLE_BLOCK, // Assume a single uniform block of text
        preserve_interword_spaces: '1',
        tessedit_ocr_engine_mode: '3', // LSTM mode
      });

      const { data } = await worker.recognize(imageData);
      await worker.terminate();
      return data.text;
    } catch (err) {
      console.error('OCR processing error:', err);
      throw new Error('Failed to process image with OCR');
    }
  }
  
  /**
   * Try to detect and parse Machine Readable Zone (MRZ) from passports and some ID cards
   */
    private static attemptMrzParsing(text: string): any {
        try {
            // Extract potential MRZ lines (typically at bottom of document)
            const lines = text.split('\n');
            const potentialMrzLines = lines
                .filter(line => /^[A-Z0-9<]{30,44}$/.test(line.trim()))
                .slice(-3); // Last 3 lines that match the pattern

            if (potentialMrzLines.length >= 2) {
                // Try to parse as TD1 (3-line MRZ), TD2 or TD3 (2-line MRZ)
                const mrzText = potentialMrzLines.join('\n');
                // Use mrz-parser library to parse the MRZ text
                return mrzParser.parse(mrzText); // Using the correct function from mrz-parser
            }
            return null;
        } catch (err) {
            console.warn('MRZ parsing failed, continuing with regular parsing:', err);
            return null;
        }
    }
  
  /**
   * Format MRZ data into our standard format
   */
  private static formatMrzData(mrzData: any): Partial<IdCardData> {
    if (!mrzData || !mrzData.fields) return {};
    
    try {
      const { fields } = mrzData;
      return {
        firstName: fields.firstName || '',
        lastName: fields.lastName || '',
        dateOfBirth: fields.birthDate ? this.formatDate(fields.birthDate) : '',
        documentNumber: fields.documentNumber || '',
        expiryDate: fields.expiryDate ? this.formatDate(fields.expiryDate) : '',
        nationality: fields.nationality || '',
        issuingCountry: fields.issuingCountry || '',
        gender: fields.sex === 'M' ? 'Male' : fields.sex === 'F' ? 'Female' : ''
      };
    } catch (err) {
      console.warn('Error formatting MRZ data:', err);
      return {};
    }
  }
  
  /**
   * Basic field parsing with regex patterns
   */
  private static parseBasicFields(text: string): Partial<IdCardData> {
    const result: Partial<IdCardData> = {};
    
    // Name extraction (various formats)
    const namePatterns = [
      /Name:?\s*([A-Za-z\s]+)/i,
      /Full Name:?\s*([A-Za-z\s]+)/i,
      /Given Names?:?\s*([A-Za-z\s]+)/i,
      /Surname:?\s*([A-Za-z\s]+)/i
    ];
    
    for (const pattern of namePatterns) {
      const match = text.match(pattern);
      if (match && match[1]) {
        const fullName = match[1].trim();
        const nameParts = fullName.split(' ');
        
        if (nameParts.length > 0) {
          result.firstName = result.firstName || nameParts[0];
          
          if (nameParts.length > 1) {
            result.lastName = result.lastName || nameParts.slice(1).join(' ');
          }
        }
      }
    }
    
    // Date of Birth extraction
    const dobPatterns = [
      /Birth(?:day|date|day)?:?\s*(\d{1,2}[-.\/]\d{1,2}[-.\/]\d{2,4})/i,
      /DOB:?\s*(\d{1,2}[-.\/]\d{1,2}[-.\/]\d{2,4})/i,
      /Date of Birth:?\s*(\d{1,2}[-.\/]\d{1,2}[-.\/]\d{2,4})/i,
      /Birth:?\s*(\d{1,2}[-.\/]\d{1,2}[-.\/]\d{2,4})/i
    ];
    
    for (const pattern of dobPatterns) {
      const match = text.match(pattern);
      if (match && match[1]) {
        result.dateOfBirth = match[1];
        break;
      }
    }
    
    return result;
  }
  
  /**
   * Advanced pattern-based field extraction
   */
  private static parseWithPatterns(text: string): Partial<IdCardData> {
    const result: Partial<IdCardData> = {};
    
    // Document number (ID Card, Passport, etc.)
    const docNumberMatch = text.match(/(?:ID|Document|Card|No|Number|#):?\s*([A-Z0-9]{6,12})/i);
    if (docNumberMatch && docNumberMatch[1]) {
      result.documentNumber = docNumberMatch[1];
    }
    
    // Expiration date
    const expiryMatch = text.match(/(?:Expiry|Expiration|Exp|Valid Until):?\s*(\d{1,2}[-.\/]\d{1,2}[-.\/]\d{2,4})/i);
    if (expiryMatch && expiryMatch[1]) {
      result.expiryDate = expiryMatch[1];
    }
    
    // Nationality
    const nationalityMatch = text.match(/(?:Nationality|Nation):?\s*([A-Za-z\s]+)/i);
    if (nationalityMatch && nationalityMatch[1]) {
      result.nationality = nationalityMatch[1].trim();
    }
    
    // Gender
    const genderMatch = text.match(/(?:Sex|Gender):?\s*([MF]|Male|Female)/i);
    if (genderMatch && genderMatch[1]) {
      const gender = genderMatch[1].toUpperCase();
      result.gender = gender === 'M' ? 'Male' : gender === 'F' ? 'Female' : gender;
    }
    
    return result;
  }
  
  /**
   * Combine results from different parsing methods, with priority order
   */
  private static combineResults(...dataSets: (Partial<IdCardData> | null)[]): IdCardData {
    const result: IdCardData = {
      firstName: '',
      lastName: '',
      dateOfBirth: ''
    };
    
    // Filter out null values and reverse the array to give priority to earlier items
    const validDataSets = dataSets.filter(data => data !== null) as Partial<IdCardData>[];
    
    // Combine all results, taking the first non-empty value for each field
    for (const dataSet of validDataSets) {
      Object.entries(dataSet).forEach(([key, value]) => {
        if (value && typeof value === 'string' && value.trim() !== '') {
          // @ts-ignore - We know these properties exist in our result type
          result[key] = result[key] || value.trim();
        }
      });
    }
    
    return result;
  }
  
  /**
   * Format date to a standard format
   */
  private static formatDate(dateStr: string): string {
    try {
      // Handle different date formats and normalize
      if (typeof dateStr !== 'string') return '';
      
      // Try to detect format (DD.MM.YYYY, YYYY-MM-DD, etc.)
      const cleanDate = dateStr.replace(/[^\d]/g, '');
      
      if (cleanDate.length === 8) {
        // Assume YYYYMMDD format from MRZ
        const year = cleanDate.substring(0, 4);
        const month = cleanDate.substring(4, 6);
        const day = cleanDate.substring(6, 8);
        return `${day}/${month}/${year}`;
      }
      
      // For other formats, just return as is
      return dateStr;
    } catch (err) {
      return dateStr;
    }
  }
}