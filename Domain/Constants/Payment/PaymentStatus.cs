﻿namespace Domain.Constants.Payment
{
    public static class PaymentStatus
    {
        public const string Queued = "QUEUED";
        public const string Pending = "PENDING";
        public const string Filled = "FILLED";
        public const string PartiallyFilled = "PARTIALLY_FILLED";
        public const string Failed = "FAILED";
        public const string InsufficientFunds = "INSUFFICIENTT_FUNDS";
    }
}
