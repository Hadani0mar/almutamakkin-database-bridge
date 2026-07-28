namespace Almutamakkin.DatabaseBridge.Protocol;

public static class ErrorCodes
{
    public const string InvalidMessage = "INVALID_MESSAGE";
    public const string UnsupportedProtocol = "UNSUPPORTED_PROTOCOL";
    public const string UnsupportedCommand = "UNSUPPORTED_COMMAND";
    public const string InvalidRequestId = "INVALID_REQUEST_ID";
    public const string DuplicateRequest = "DUPLICATE_REQUEST";
    public const string RequestExpired = "REQUEST_EXPIRED";
    public const string InvalidTunnelId = "INVALID_TUNNEL_ID";
    public const string DatabaseProfileNotFound = "DATABASE_PROFILE_NOT_FOUND";
    public const string DatabaseProfileDisabled = "DATABASE_PROFILE_DISABLED";
    public const string DatabaseConnectionFailed = "DATABASE_CONNECTION_FAILED";
    public const string SqlEmpty = "SQL_EMPTY";
    public const string SqlTooLong = "SQL_TOO_LONG";
    public const string SqlClassificationFailed = "SQL_CLASSIFICATION_FAILED";
    public const string SqlPermissionDenied = "SQL_PERMISSION_DENIED";
    public const string SqlTimeout = "SQL_TIMEOUT";
    public const string SqlExecutionFailed = "SQL_EXECUTION_FAILED";
    public const string ResultTooLarge = "RESULT_TOO_LARGE";
    public const string ResultTruncated = "RESULT_TRUNCATED";
    public const string BridgeOffline = "BRIDGE_OFFLINE";
    public const string BridgeBusy = "BRIDGE_BUSY";
    public const string InternalError = "INTERNAL_ERROR";
    public const string PrinterNotConfigured = "PRINTER_NOT_CONFIGURED";
    public const string PrinterNotReady = "PRINTER_NOT_READY";
    public const string PrinterProductNotFound = "PRINTER_PRODUCT_NOT_FOUND";
    public const string PrinterNotPrintable = "PRINTER_NOT_PRINTABLE";
    public const string PrinterQueueFull = "PRINTER_QUEUE_FULL";
    public const string PrinterConflict = "PRINTER_CONFLICT";
    public const string ProductPhotoNotFound = "PRODUCT_PHOTO_NOT_FOUND";
    public const string ProductPhotoFailed = "PRODUCT_PHOTO_FAILED";
    public const string ProductPhotoWriteDisabled = "PRODUCT_PHOTO_WRITE_DISABLED";
}
