using Domain.Constants.Transaction;
using Domain.DTOs.Transaction;
using Domain.Models.Transaction;

namespace Infrastructure.Services.Transaction
{
    /// <summary>
    /// Fluent builder for creating properly structured double-entry transactions
    /// Ensures all required fields are set and validation rules are followed
    /// </summary>
    public class TransactionBuilder
    {
        private readonly TransactionData _transaction;

        private TransactionBuilder()
        {
            _transaction = new TransactionData
            {
                UserId = Guid.Empty,
                SourceName = string.Empty,
                SourceId = string.Empty,
                Action = string.Empty,
                IsConfirmed = false
            };
        }

        /// <summary>
        /// Creates a new transaction builder
        /// </summary>
        public static TransactionBuilder Create() => new TransactionBuilder();

        // ===== Core Transaction Properties =====

        public TransactionBuilder WithUser(Guid userId)
        {
            _transaction.UserId = userId;
            return this;
        }

        public TransactionBuilder WithAction(string action)
        {
            _transaction.Action = action;
            return this;
        }

        public TransactionBuilder WithSource(string sourceName, string sourceId)
        {
            _transaction.SourceName = sourceName;
            _transaction.SourceId = sourceId;
            return this;
        }

        public TransactionBuilder WithDescription(string description)
        {
            _transaction.Description = description;
            return this;
        }

        public TransactionBuilder WithPaymentProvider(string paymentProviderId)
        {
            _transaction.PaymentProviderId = paymentProviderId;
            return this;
        }

        public TransactionBuilder WithSubscription(Guid subscriptionId)
        {
            _transaction.SubscriptionId = subscriptionId;
            return this;
        }

        // ===== Double-Entry Setup =====

        public TransactionBuilder WithFromBalance(
            Guid userId,
            Guid assetId,
            string ticker,
            decimal quantity,
            BalanceType balanceType = BalanceType.Available,
            Guid? balanceId = null)
        {
            _transaction.FromBalance = new TransactionEntry
            {
                UserId = userId,
                AssetId = assetId,
                Ticker = ticker,
                Quantity = -Math.Abs(quantity), // Always negative for "from"
                BalanceType = balanceType,
                BalanceId = balanceId
            };
            return this;
        }

        public TransactionBuilder WithToBalance(
            Guid userId,
            Guid assetId,
            string ticker,
            decimal quantity,
            BalanceType balanceType = BalanceType.Available,
            Guid? balanceId = null)
        {
            _transaction.ToBalance = new TransactionEntry
            {
                UserId = userId,
                AssetId = assetId,
                Ticker = ticker,
                Quantity = Math.Abs(quantity), // Always positive for "to"
                BalanceType = balanceType,
                BalanceId = balanceId
            };
            return this;
        }

        public TransactionBuilder WithFee(
            Guid userId,
            Guid assetId,
            string ticker,
            decimal feeAmount,
            Guid? feeRecipientUserId = null)
        {
            // Fee is deducted from the user
            _transaction.Fee = new TransactionEntry
            {
                UserId = feeRecipientUserId ?? userId,
                AssetId = assetId,
                Ticker = ticker,
                Quantity = Math.Abs(feeAmount),
                BalanceType = BalanceType.Available
            };
            return this;
        }

        public TransactionBuilder WithRounding(
            Guid userId,
            Guid assetId,
            string ticker,
            decimal roundingAmount)
        {
            _transaction.Rounding = new TransactionEntry
            {
                UserId = userId,
                AssetId = assetId,
                Ticker = ticker,
                Quantity = roundingAmount, // Can be positive or negative
                BalanceType = BalanceType.Available
            };
            return this;
        }

        // ===== Balance Snapshots =====

        public TransactionBuilder WithFromBalanceSnapshot(
            decimal beforeAvailable,
            decimal beforeLocked,
            decimal afterAvailable,
            decimal afterLocked)
        {
            if (_transaction.FromBalance != null)
            {
                _transaction.FromBalance.BalanceBeforeAvailable = beforeAvailable;
                _transaction.FromBalance.BalanceBeforeLocked = beforeLocked;
                _transaction.FromBalance.BalanceAfterAvailable = afterAvailable;
                _transaction.FromBalance.BalanceAfterLocked = afterLocked;
            }
            return this;
        }

        public TransactionBuilder WithToBalanceSnapshot(
            decimal beforeAvailable,
            decimal beforeLocked,
            decimal afterAvailable,
            decimal afterLocked)
        {
            if (_transaction.ToBalance != null)
            {
                _transaction.ToBalance.BalanceBeforeAvailable = beforeAvailable;
                _transaction.ToBalance.BalanceBeforeLocked = beforeLocked;
                _transaction.ToBalance.BalanceAfterAvailable = afterAvailable;
                _transaction.ToBalance.BalanceAfterLocked = afterLocked;
            }
            return this;
        }

        // ===== Confirmation =====

        public TransactionBuilder AsConfirmed()
        {
            _transaction.IsConfirmed = true;
            _transaction.ConfirmedAt = DateTime.UtcNow;
            return this;
        }

        // ===== Reversal Support =====

        public TransactionBuilder AsReversalOf(Guid originalTransactionId)
        {
            _transaction.ReversalOfTransactionId = originalTransactionId;
            return this;
        }

        // ===== Build =====

        public TransactionData Build()
        {
            var (isValid, errorMessage) = _transaction.Validate();
            if (!isValid)
            {
                throw new InvalidOperationException($"Invalid transaction: {errorMessage}");
            }

            return _transaction;
        }

        /// <summary>
        /// Builds without validation (use cautiously)
        /// </summary>
        public TransactionData BuildUnsafe()
        {
            return _transaction;
        }
    }

    /// <summary>
    /// Pre-configured transaction builders for common scenarios
    /// </summary>
    public static class CommonTransactions
    {
        /// <summary>
        /// External deposit (e.g., Stripe payment)
        /// Money coming into the system from outside
        /// Example: User receives $100 USD via Stripe
        /// </summary>
        public static TransactionBuilder ExternalDeposit(
            Guid userId,
            Guid assetId,
            Guid balanceId,
            string ticker,
            decimal amount,
            string sourceName,
            string sourceId,
            string? paymentProviderId = null)
        {
            var builder = TransactionBuilder.Create()
                .WithUser(userId)
                .WithAction(TransactionActionType.Deposit)
                .WithSource(sourceName, sourceId)
                .WithToBalance(userId, assetId, ticker, amount, BalanceType.Available, balanceId)
                .WithDescription($"External deposit of {amount} {ticker} from {sourceName}");

            if (!string.IsNullOrEmpty(paymentProviderId))
            {
                builder.WithPaymentProvider(paymentProviderId);
            }

            return builder;
        }

        /// <summary>
        /// Platform fee deduction
        /// Example: $1 USD fee from user to corporate account
        /// </summary>
        public static TransactionBuilder PlatformFee(
            Guid userId,
            Guid corporateUserId,
            Guid assetId,
            string ticker,
            decimal feeAmount,
            string description = "Platform fee")
        {
            return TransactionBuilder.Create()
                .WithUser(userId)
                .WithAction(TransactionActionType.Fee)
                .WithSource("Platform", Guid.NewGuid().ToString())
                .WithFromBalance(userId, assetId, ticker, feeAmount)
                .WithToBalance(corporateUserId, assetId, ticker, feeAmount)
                .WithDescription(description);
        }

        /// <summary>
        /// Crypto purchase using fiat
        /// Example: Buy 0.000249 ETH using 99 USD
        /// </summary>
        public static TransactionBuilder CryptoPurchase(
            Guid userId,
            Guid fiatBalanceId,
            Guid fiatAssetId,
            string fiatTicker,
            decimal fiatAmount,
            Guid cryptoBalanceId,
            Guid cryptoAssetId,
            string cryptoTicker,
            decimal cryptoAmount,
            string exchange,
            string orderId,
            decimal? exchangeFee = null,
            Guid? feeAssetId = null,
            string? feeTicker = null)
        {
            var builder = TransactionBuilder.Create()
                .WithUser(userId)
                .WithAction(TransactionActionType.Buy)
                .WithSource(exchange, orderId)
                .WithFromBalance(userId, fiatAssetId, fiatTicker, fiatAmount, BalanceType.Available, fiatBalanceId)
                .WithToBalance(userId, cryptoAssetId, cryptoTicker, cryptoAmount, BalanceType.Available, cryptoBalanceId)
                .WithDescription($"Buy {cryptoAmount} {cryptoTicker} for {fiatAmount} {fiatTicker} on {exchange}");

            if (exchangeFee.HasValue && exchangeFee.Value > 0 && feeAssetId.HasValue)
            {
                builder.WithFee(userId, feeAssetId.Value, feeTicker ?? fiatTicker, exchangeFee.Value);
            }

            return builder;
        }

        /// <summary>
        /// Crypto sale to fiat
        /// Example: Sell 0.5 ETH for 1,500 USD
        /// </summary>
        public static TransactionBuilder CryptoSale(
            Guid userId,
            Guid cryptoBalanceId,
            Guid cryptoAssetId,
            string cryptoTicker,
            decimal cryptoAmount,
            Guid fiatBalanceId,
            Guid fiatAssetId,
            string fiatTicker,
            decimal fiatAmount,
            string exchange,
            string orderId,
            decimal? exchangeFee = null)
        {
            var builder = TransactionBuilder.Create()
                .WithUser(userId)
                .WithAction(TransactionActionType.Sell)
                .WithSource(exchange, orderId)
                .WithFromBalance(userId, cryptoAssetId, cryptoTicker, cryptoAmount, BalanceType.Available, cryptoBalanceId)
                .WithToBalance(userId, fiatAssetId, fiatTicker, fiatAmount, BalanceType.Available, fiatBalanceId)
                .WithDescription($"Sell {cryptoAmount} {cryptoTicker} for {fiatAmount} {fiatTicker} on {exchange}");

            if (exchangeFee.HasValue && exchangeFee.Value > 0)
            {
                builder.WithFee(userId, fiatAssetId, fiatTicker, exchangeFee.Value);
            }

            return builder;
        }

        /// <summary>
        /// User-to-user transfer
        /// Example: User A sends 50 USDT to User B
        /// </summary>
        public static TransactionBuilder UserTransfer(
            Guid fromUserId,
            Guid toUserId,
            Guid assetId,
            string ticker,
            decimal amount,
            decimal? transferFee = null,
            Guid? feeRecipientUserId = null)
        {
            var builder = TransactionBuilder.Create()
                .WithUser(fromUserId)
                .WithAction(TransactionActionType.Transfer)
                .WithSource("Internal", Guid.NewGuid().ToString())
                .WithFromBalance(fromUserId, assetId, ticker, amount)
                .WithToBalance(toUserId, assetId, ticker, amount)
                .WithDescription($"Transfer {amount} {ticker} from user {fromUserId} to {toUserId}");

            if (transferFee.HasValue && transferFee.Value > 0)
            {
                builder.WithFee(fromUserId, assetId, ticker, transferFee.Value, feeRecipientUserId);
            }

            return builder;
        }

        /// <summary>
        /// External withdrawal (money leaving the system)
        /// Example: User withdraws 100 USDT to external wallet
        /// </summary>
        public static TransactionBuilder ExternalWithdrawal(
            Guid userId,
            Guid balanceId,
            Guid assetId,
            string ticker,
            decimal amount,
            string destination,
            string transactionHash,
            decimal? networkFee = null)
        {
            var builder = TransactionBuilder.Create()
                .WithUser(userId)
                .WithAction(TransactionActionType.Withdrawal)
                .WithSource("Blockchain", transactionHash)
                .WithFromBalance(userId, assetId, ticker, amount, BalanceType.Available, balanceId)
                .WithDescription($"Withdrawal of {amount} {ticker} to {destination}");

            if (networkFee.HasValue && networkFee.Value > 0)
            {
                builder.WithFee(userId, assetId, ticker, networkFee.Value);
            }

            return builder;
        }

        /// <summary>
        /// Lock funds (move from available to locked)
        /// Example: Lock 100 USDT for a pending order
        /// </summary>
        public static TransactionBuilder LockFunds(
            Guid userId,
            Guid balanceId,
            Guid assetId,
            string ticker,
            decimal amount,
            string reason,
            string referenceId)
        {
            return TransactionBuilder.Create()
                .WithUser(userId)
                .WithAction(TransactionActionType.Lock)
                .WithSource("System", referenceId)
                .WithFromBalance(userId, assetId, ticker, amount, BalanceType.Available, balanceId)
                .WithToBalance(userId, assetId, ticker, amount, BalanceType.Locked, balanceId)
                .WithDescription($"Lock {amount} {ticker} for {reason}");
        }

        /// <summary>
        /// Unlock funds (move from locked to available)
        /// Example: Release 100 USDT after order completion
        /// </summary>
        public static TransactionBuilder UnlockFunds(
            Guid userId,
            Guid balanceId,
            Guid assetId,
            string ticker,
            decimal amount,
            string reason,
            string referenceId)
        {
            return TransactionBuilder.Create()
                .WithUser(userId)
                .WithAction(TransactionActionType.Unlock)
                .WithSource("System", referenceId)
                .WithFromBalance(userId, assetId, ticker, amount, BalanceType.Locked, balanceId)
                .WithToBalance(userId, assetId, ticker, amount, BalanceType.Available, balanceId)
                .WithDescription($"Unlock {amount} {ticker} for {reason}");
        }

        /// <summary>
        /// Refund/Reversal transaction
        /// Creates a reversal of an original transaction
        /// </summary>
        public static TransactionBuilder Refund(
            TransactionData originalTransaction,
            string reason)
        {
            var builder = TransactionBuilder.Create()
                .WithUser(originalTransaction.UserId)
                .WithAction($"{originalTransaction.Action}_REFUND")
                .WithSource("System", $"REFUND_{originalTransaction.Id}")
                .AsReversalOf(originalTransaction.Id)
                .WithDescription($"Refund: {reason}");

            // Reverse the entries
            if (originalTransaction.FromBalance != null)
            {
                builder.WithToBalance(
                    originalTransaction.FromBalance.UserId,
                    originalTransaction.FromBalance.AssetId,
                    originalTransaction.FromBalance.Ticker ?? "",
                    Math.Abs(originalTransaction.FromBalance.Quantity),
                    originalTransaction.FromBalance.BalanceType,
                    originalTransaction.FromBalance.BalanceId);
            }

            if (originalTransaction.ToBalance != null)
            {
                builder.WithFromBalance(
                    originalTransaction.ToBalance.UserId,
                    originalTransaction.ToBalance.AssetId,
                    originalTransaction.ToBalance.Ticker ?? "",
                    originalTransaction.ToBalance.Quantity,
                    originalTransaction.ToBalance.BalanceType,
                    originalTransaction.ToBalance.BalanceId);
            }

            return builder;
        }
    }
}