using Infrastructure.Services.FlowEngine.Core.Models;
using Infrastructure.Services.FlowEngine.Core.PauseResume;

namespace Infrastructure.Services.FlowEngine.Definition.Builders
{
    /// <summary>
    /// Builder for configuring resume conditions and triggers
    /// </summary>
    public class ResumeConfigBuilder
    {
        private readonly ResumeConfig _config;

        internal ResumeConfigBuilder(ResumeConfig config)
        {
            _config = config;
        }

        /// <summary>
        /// Allow manual resume by users/admins
        /// </summary>
        public ResumeConfigBuilder AllowManual(params string[] allowedRoles)
        {
            _config.AllowManualResume = true;
            _config.AllowedRoles.AddRange(allowedRoles);
            return this;
        }

        /// <summary>
        /// Resume when specific event is published
        /// </summary>
        public ResumeConfigBuilder OnEvent(string eventType, Func<object, bool> eventFilter = null)
        {
            _config.EventTriggers.Add(new EventTrigger
            {
                EventType = eventType,
                EventFilter = eventFilter
            });
            return this;
        }

        /// <summary>
        /// Resume when condition becomes true (checked periodically)
        /// </summary>
        public ResumeConfigBuilder WhenCondition(Func<FlowContext, Task<bool>> condition, TimeSpan? checkInterval = null)
        {
            _config.AutoResumeCondition = condition;
            _config.ConditionCheckInterval = checkInterval ?? TimeSpan.FromMinutes(5);
            return this;
        }

        /// <summary>
        /// Resume after a timeout period
        /// </summary>
        public ResumeConfigBuilder AfterTimeout(TimeSpan timeout, bool resumeOnTimeout = true)
        {
            _config.TimeoutDuration = timeout;
            _config.ResumeOnTimeout = resumeOnTimeout;
            return this;
        }

        /// <summary>
        /// Resume when external API call succeeds
        /// </summary>
        public ResumeConfigBuilder OnApiSuccess(Func<FlowContext, Task<bool>> apiCheck, TimeSpan checkInterval)
        {
            _config.AutoResumeCondition = apiCheck;
            _config.ConditionCheckInterval = checkInterval;
            return this;
        }
    }
}
