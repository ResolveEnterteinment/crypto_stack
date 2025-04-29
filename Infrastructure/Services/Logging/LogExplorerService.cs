using Domain.Models.Logging;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;

namespace Infrastructure.Services.Logging
{
    public class LogExplorerService
    {
        private readonly IMongoCollection<TraceLog> _collection;

        public LogExplorerService(IConfiguration config)
        {
            var client = new MongoClient(config.GetConnectionString("MongoDb"));
            var db = client.GetDatabase("LogsDb");
            _collection = db.GetCollection<TraceLog>("TraceLogs");
        }

        public async Task<List<TraceLogNodeData>> GetTraceTreeAsync(Guid? rootId = null)
        {
            var allLogs = await _collection.Find(_ => true).ToListAsync();

            var logMap = allLogs.ToDictionary(log => log.Id);
            var nodeMap = allLogs.ToDictionary(log => log.Id, log => new TraceLogNodeData { Log = log });

            List<TraceLogNodeData> roots = new();

            foreach (var log in allLogs)
            {
                if (log.ParentCorrelationId.HasValue && nodeMap.ContainsKey(log.ParentCorrelationId.Value))
                {
                    nodeMap[log.ParentCorrelationId.Value].Children.Add(nodeMap[log.Id]);
                }
                else if (rootId == null || log.Id == rootId)
                {
                    roots.Add(nodeMap[log.Id]);
                }
            }

            return roots;
        }
    }
}
