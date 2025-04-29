namespace Infrastructure.Services.Logging
{
    using Serilog.Core;
    using Serilog.Events;
    using System.Diagnostics;

    public class TracingEnricher : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            var activity = Activity.Current;

            if (activity != null)
            {
                if (!string.IsNullOrEmpty(activity.TraceId.ToString()))
                {
                    logEvent.AddPropertyIfAbsent(
                        propertyFactory.CreateProperty("TraceId", activity.TraceId.ToString()));
                }

                if (!string.IsNullOrEmpty(activity.SpanId.ToString()))
                {
                    logEvent.AddPropertyIfAbsent(
                        propertyFactory.CreateProperty("SpanId", activity.SpanId.ToString()));
                }
            }
        }
    }
}
