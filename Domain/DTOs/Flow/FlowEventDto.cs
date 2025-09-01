using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.DTOs.Flow
{
    public class FlowEventDto
    {
        public Guid EventId { get; set; }
        public Guid FlowId { get; set; }
        public string EventType { get; set; }
        public string Description { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public Dictionary<string, object> Data { get; set; } = [];
    }
}
