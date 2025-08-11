namespace Domain.Exceptions
{
    /// <summary>
    /// Exception thrown when dashboard operations fail
    /// </summary>
    public class DashboardException : DomainException
    {
        public DashboardException(string message) : base(message, "DASHBOARD_ERROR")
        {
        }

        public DashboardException(string message, Exception innerException) : base(message, "DASHBOARD_ERROR", innerException)
        {
        }
    }
}
