using Domain.DTOs.Event;
using Domain.Models.Withdrawal;
using MediatR;

namespace Domain.Events
{
    // Event for MediatR
    public class WithdrawalApprovedEvent : BaseEvent, INotification
    {
        public WithdrawalData Withdrawal;

        public DateTime CurrentPeriodEnd;
        public WithdrawalApprovedEvent(WithdrawalData withdrawal, IDictionary<string, object?> context = null) :
            base(context)
        {
            Withdrawal = withdrawal;
        }
    }
}