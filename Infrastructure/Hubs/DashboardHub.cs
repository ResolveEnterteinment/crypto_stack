using Application.Interfaces;
using Domain.DTOs;
using Domain.Models.Balance;
using Domain.Models.Crypto;
using Microsoft.AspNetCore.SignalR;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Infrastructure.Hubs
{
    public class DashboardHub : Hub
    {
        private readonly IMongoCollection<BalanceData> _balances;
        private readonly IMongoCollection<AssetData> _assets;
        private readonly IHubContext<DashboardHub> _hubContext;
        private readonly IDashboardService _dashboardService;

        public DashboardHub(IMongoDatabase database, IHubContext<DashboardHub> hubContext, IDashboardService dashboardService)
        {
            _balances = database.GetCollection<BalanceData>("Balances");
            _assets = database.GetCollection<AssetData>("Assets");
            _hubContext = hubContext;
            _dashboardService = dashboardService;
            StartChangeStream();
        }

        public async Task SubscribeToUpdates(Guid userId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, userId.ToString());
            var data = await _dashboardService.GetDashboardDataAsync(userId);
            await Clients.Caller.SendAsync("DashboardUpdate", data);
        }

        private void StartChangeStream()
        {
            var pipeline = new EmptyPipelineDefinition<ChangeStreamDocument<BalanceData>>()
                .Match(change => change.OperationType == ChangeStreamOperationType.Insert ||
                               change.OperationType == ChangeStreamOperationType.Update);

            var options = new ChangeStreamOptions { FullDocument = ChangeStreamFullDocumentOption.UpdateLookup };
            var cursor = _balances.Watch(pipeline, options);

            Task.Run(async () =>
            {
                while (await cursor.MoveNextAsync())
                {
                    foreach (var change in cursor.Current)
                    {
                        var updatedBalance = change.FullDocument;

                        // Define projection
                        var projection = Builders<BsonDocument>.Projection
                            .Expression(doc => new BalanceDto
                            {
                                AssetName = doc["AssetDocs"]["Name"].AsString,
                                Ticker = doc["AssetDocs"]["Ticker"].AsString,
                                Available = doc["Available"].AsDecimal,
                                Locked = doc["Locked"].AsDecimal,
                                Total = doc["Total"].AsDecimal
                            });

                        var balanceDto = await _balances.Aggregate()
                            .Match(b => b.Id == updatedBalance.Id)
                            .Lookup<BalanceData, AssetData, BalanceData>(
                                foreignCollection: _assets,
                                localField: l => l.AssetId,
                                foreignField: f => f.Id,
                                @as: t => t.AssetDocs
                            )
                            .Unwind(b => b.AssetDocs)
                            .As<BsonDocument>() // Convert to BsonDocument
                            .Project<BalanceDto>(projection) // Apply projection
                            .FirstOrDefaultAsync();

                        var userId = updatedBalance.UserId;
                        //await _dashboardService.UpdateDashboardData(userId, -100);
                        var dashboardData = await _dashboardService.GetDashboardDataAsync(userId);
                        await _hubContext.Clients.Group(userId.ToString()).SendAsync("DashboardUpdate", dashboardData);
                    }
                }
            });
        }
    }
}