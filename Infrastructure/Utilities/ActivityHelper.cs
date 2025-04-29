using System.Diagnostics;

namespace Infrastructure.Utilities
{
    public static class ActivityHelper
    {
        private static readonly ActivitySource _activitySource = new("CryptoInvestmentProject");

        public static Activity? StartActivity(string operationName, IDictionary<string, object?>? tags = null)
        {
            var parentActivity = Activity.Current;

            ActivityContext parentContext = parentActivity != null
                ? parentActivity.Context
                : default;

            var activity = _activitySource.StartActivity(operationName, ActivityKind.Internal, parentContext);

            if (activity != null && tags != null)
            {
                foreach (var kv in tags)
                {
                    activity.SetTag(kv.Key, kv.Value?.ToString());
                }
            }

            return activity;
        }

    }
}
