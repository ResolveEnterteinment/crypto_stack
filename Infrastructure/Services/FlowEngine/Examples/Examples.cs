/*
// DEAD SIMPLE SETUP - ONE TIME CONFIGURATION
var services = FlowEngine.Configure()
    .UseMongoDB("mongodb://localhost:27017", "MyApp")
    .WithSecurity(security => 
    {
        security.EnableEncryption = true;
        security.EnableAuditLog = true;
        security.RequireUserAuthorization = true;
    })
    .WithRecovery(TimeSpan.FromMinutes(5), enableStartupRecovery: true)
    .UseMiddleware<CustomSecurityMiddleware>()
    .AddServices(services => 
    {
        services.AddScoped<IMyCustomService, MyCustomService>();
    })
    .Build();

// EXAMPLE 1: CRYPTO PAYMENT WITH BALANCE CHECK (Pause/Resume)
var result = await FlowEngine.Start<CryptoPaymentWithBalanceCheckFlow>(new 
{ 
    Amount = 5000.00m, 
    Currency = "BTC",
    UserId = "user123"
}, userId: "user123");

// Flow will automatically:
// 1. Validate payment
// 2. Check exchange balance
// 3. IF INSUFFICIENT -> Pause and request top-up
// 4. Resume when balance top-up event is published OR admin manually resumes
// 5. Execute trade when resumed
// 6. IF large trade -> Pause for compliance approval
// 7. Resume when compliance approves
// 8. Send confirmation

// EXAMPLE 2: PUBLISHING EVENTS TO RESUME FLOWS
await FlowEngine.PublishEvent("BalanceTopUp", new BalanceTopUpEvent 
{
    Currency = "BTC",
    Amount = 2000.00m,
    Timestamp = DateTime.UtcNow
});

await FlowEngine.PublishEvent("ComplianceApproval", new ComplianceApprovalEvent
{
    TradeId = "trade_123",
    Approved = true,
    ReviewedBy = "compliance_officer_jane"
});

// EXAMPLE 3: MANUAL RESUME (Admin Dashboard)
var pausedFlows = await FlowEngine.GetPausedFlows(new FlowQuery 
{ 
    PauseReason = PauseReason.InsufficientResources 
});

foreach (var flow in pausedFlows.Items)
{
    // Admin decides to manually resume
    await FlowEngine.ResumeManually(flow.FlowId, "admin_john", "Manual override - balance confirmed");
}

// EXAMPLE 4: AUTO-RESUME CONDITIONS (Background Service)
var resumedCount = await FlowEngine.CheckAutoResumeConditions();
Console.WriteLine($"Auto-resumed {resumedCount} flows");

// EXAMPLE 5: DYNAMIC PAYMENT ALLOCATION WITH PAUSE/RESUME
await FlowEngine.Start<PaymentAllocationFlow>(new 
{
    Amount = 1500.00m, 
    SubscriptionId = "sub_123",
    UserId = "user123"
}, userId: "user123");

// EXAMPLE 6: API RATE LIMIT HANDLING
await FlowEngine.Start<ApiIntegrationFlow>(new { ApiEndpoint = "/api/data" });
// Flow will pause when rate limit is hit and auto-resume after the timeout

// EXAMPLE 7: QUERY PAUSED FLOWS BY REASON
var balanceIssues = await FlowEngine.GetPausedFlows(new FlowQuery 
{
    PauseReason = PauseReason.InsufficientResources,
    CreatedAfter = DateTime.Today.AddDays(-7)
});

var pendingApprovals = await FlowEngine.GetPausedFlows(new FlowQuery 
{
    PauseReason = PauseReason.ManualApproval,
    UserId = "compliance_team"
});

// EXAMPLE 8: FIRE AND FORGET WITH PAUSE CAPABILITY
await FlowEngine.Fire<CryptoPaymentWithBalanceCheckFlow>(new { 
    Amount = 500.00m, 
    Currency = "ETH" 
});
// Even fire-and-forget flows can pause and resume automatically

// EXAMPLE 9: RESUME FROM CRASH (All pause states preserved)
var resumed = await FlowEngine.Resume<CryptoPaymentWithBalanceCheckFlow>("flow-id-123");

// EXAMPLE 10: SET CUSTOM RESUME CONDITION
await FlowEngine.SetResumeCondition("flow-id-456", new ResumeCondition
{
    FlowId = "flow-id-456",
    Condition = async ctx =>
    {
        // Custom business logic to check if flow should resume
        var exchangeService = ctx.Services.GetService<IExchangeService>();
        var balance = await exchangeService.GetBalanceAsync("BTC");
        return balance > 1000;
    },
    CheckInterval = TimeSpan.FromMinutes(10)
});

// REAL-WORLD SCENARIOS HANDLED:

// SCENARIO 1: Crypto Exchange Insufficient Balance
// Payment: $10,000 → Split into 4 assets
// Problem: Exchange only has $3,000 BTC
// Solution: Pause → Request top-up → Auto-resume when balance event received

// SCENARIO 2: Large Trade Compliance
// Payment: $50,000 → Requires compliance approval
// Solution: Pause → Compliance officer approves → Resume automatically

// SCENARIO 3: API Rate Limits
// Integration: Pulling data from external API
// Problem: Hit rate limit (429 error)
// Solution: Pause → Auto-resume after retry-after timeout

// SCENARIO 4: System Maintenance
// Flow: Processing payments during maintenance window
// Solution: Pause all flows → Resume after maintenance complete

// SCENARIO 5: Data Dependency
// Flow: Waiting for daily exchange rates
// Solution: Pause until 9 AM EST → Auto-resume when rates available

// KEY BENEFITS:
// ✅ Indefinite pause duration - flows can wait days/weeks/months
// ✅ Multiple resume triggers - events, conditions, manual, timeout
// ✅ Persistent state - all data preserved across restarts
// ✅ Background monitoring - automatic condition checking
// ✅ Event-driven - external systems can trigger resumes
// ✅ Role-based manual resume - security controls
// ✅ Rich pause context - full metadata about why paused
// ✅ Timeline tracking - complete audit trail
// ✅ Recovery support - crashed paused flows are recovered
*/