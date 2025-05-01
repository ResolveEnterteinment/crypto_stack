import ITraceLog from "./ITraceLog";

/**
 * Interface for a trace log node in the hierarchical tree structure
 */
export default interface ITraceLogNode {
    /**
     * The log entry data
     */
    log: ITraceLog;

    /**
     * Child log entries (related logs)
     */
    children: ITraceLogNode[];
}