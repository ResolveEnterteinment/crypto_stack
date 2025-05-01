// First, define LogLevel (from Microsoft.Extensions.Logging.LogLevel)
export enum LogLevel {
    TRACE = 0,
    DEBUG = 1,
    INFORMATION = 2,
    WARNING = 3,
    ERROR = 4,
    CRITICAL = 5,
    NONE = 6,

}

    /*
    | 'Trace'
    | 'Debug'
    | 'Information'
    | 'Warning'
    | 'Error'
    | 'Critical'
    | 'None';
    */

// Allowed resolution statuses
export enum ResolutionStatus {
    UNRESOLVED = 0,
    ACKNOWLEDGED = 1,
    RECONCILED = 2
}

/**
 * Interface for a trace log entry
 */
export default interface ITraceLog {
    /**
     * Unique identifier for the log entry
     */
    id: string;

    /**
     * Timestamp when the log was created
     */
    createdAt: string;

    correlationId?: string | null;
    parentCorrelationId?: string | null;

    /**
     * Log message content
     */
    message: string;

    /**
     * Log level (0=Trace, 1=Debug, 2=Warning, 3=Error, 4=Critical)
     */
    level: LogLevel;

    /**
     * Name of the component or service that generated the log
     */
    operation?: string;

    /**
     * Additional structured data related to the log
     */
    context?: { [key: string]: string } | null;

    /**
     * Whether the log requires manual resolution
     */
    requiresResolution: boolean;

    /**
     * Whether the log has been resolved
     */
    resolutionStatus?: ResolutionStatus | null;

    /**
     * Resolution text if the log has been resolved
     */
    resolutionComment?: string | null;

    /**
     * User who resolved the log
     */
    resolvedBy?: string;

    /**
     * Timestamp when the log was resolved
     */
    resolvedAt?: string;
}