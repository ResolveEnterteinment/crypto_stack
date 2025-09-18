using Infrastructure.Services.FlowEngine.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Flows.Exchange
{
    public class HandleDustFlow : FlowDefinition
    {
        protected override void DefineSteps()
        {
            //throw new NotImplementedException();
            _builder.Step("HandleDust")
                .Execute(async context =>
                {
                    Task.Delay(1000);  // Simulate some async work

                    return StepResult.Success("Not implemented yet. Simulating dust handling logic");
                })
                .Build();
        }
    }
}
