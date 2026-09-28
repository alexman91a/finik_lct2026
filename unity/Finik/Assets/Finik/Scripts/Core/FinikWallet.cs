using System;
using System.Collections.Generic;

namespace Finik.Core
{
    public enum FinikTransactionKind
    {
        Earn,
        Spend,
        Save,
        Withdraw,
        Reserve,
        Allocate
    }

    /// <summary>Where a wallet transaction came from (same ids as the web app's WalletTransactionSource).</summary>
    public static class FinikTransactionSource
    {
        public const string Task = "task";
        public const string PetCare = "pet-care";
        public const string Goal = "goal";
        public const string PeriodIncome = "period-income";
        public const string Budget = "budget";
        public const string Purchase = "purchase";
        public const string Demo = "demo";
    }

    [Serializable]
    public sealed class FinikTransaction
    {
        public string id;
        public FinikTransactionKind kind;
        public int amount;
        public int delta;
        public string source;
        public string reason;
        public string periodId;
        public long createdAtMs;
    }

    public enum FinikWalletError
    {
        None,
        InvalidAmount,
        InsufficientFunds,
        InsufficientSavings,
        DuplicateId
    }

    /// <summary>
    /// Coins the player can spend plus coins parked in savings. Every change is an append-only
    /// transaction; the balances are always derivable from the log (port of src/domain/walletEngine.ts).
    /// </summary>
    [Serializable]
    public sealed class FinikWallet
    {
        public int balance;
        public int savingsBalance;
        public List<FinikTransaction> transactions = new();

        [NonSerialized] HashSet<string> ids;

        public bool Contains(string transactionId)
        {
            if (ids == null)
            {
                ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var t in transactions) if (t != null && t.id != null) ids.Add(t.id);
            }
            return transactionId != null && ids.Contains(transactionId);
        }

        internal void Append(FinikTransaction transaction)
        {
            transactions.Add(transaction);
            if (ids != null) ids.Add(transaction.id);
        }

        internal void InvalidateIndex() => ids = null;
    }

    public static class FinikWalletEngine
    {
        public const int DailyLoginIncome = 30;
        public const string DailyLoginReason = "Награда за первый вход сегодня";
        public const int PeriodBaseIncome = DailyLoginIncome;
        public const string PeriodIncomeReason = DailyLoginReason;

        public static int TransactionDelta(FinikTransactionKind kind, int amount) =>
            kind == FinikTransactionKind.Earn || kind == FinikTransactionKind.Withdraw ? amount : -amount;

        public static int SavingsDelta(FinikTransactionKind kind, int amount) =>
            kind == FinikTransactionKind.Save ? amount : kind == FinikTransactionKind.Withdraw ? -amount : 0;

        public static bool IsValidAmount(int amount) => amount > 0;

        static bool IsDebit(FinikTransactionKind kind) =>
            kind == FinikTransactionKind.Spend || kind == FinikTransactionKind.Save ||
            kind == FinikTransactionKind.Reserve || kind == FinikTransactionKind.Allocate;

        public static FinikTransaction Make(string id, FinikTransactionKind kind, int amount, string source, string reason, string periodId, long createdAtMs) => new()
        {
            id = id,
            kind = kind,
            amount = amount,
            delta = TransactionDelta(kind, amount),
            source = source,
            reason = reason,
            periodId = periodId,
            createdAtMs = createdAtMs
        };

        /// <summary>Validates, then applies atomically: on error the wallet is untouched.</summary>
        public static FinikWalletError Apply(FinikWallet wallet, FinikTransaction transaction)
        {
            if (wallet == null) throw new ArgumentNullException(nameof(wallet));
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));
            if (!IsValidAmount(transaction.amount)) return FinikWalletError.InvalidAmount;
            if (string.IsNullOrEmpty(transaction.id) || wallet.Contains(transaction.id)) return FinikWalletError.DuplicateId;
            if (IsDebit(transaction.kind) && wallet.balance < transaction.amount) return FinikWalletError.InsufficientFunds;
            if (transaction.kind == FinikTransactionKind.Withdraw && wallet.savingsBalance < transaction.amount) return FinikWalletError.InsufficientSavings;

            wallet.balance += TransactionDelta(transaction.kind, transaction.amount);
            wallet.savingsBalance += SavingsDelta(transaction.kind, transaction.amount);
            wallet.Append(transaction);
            return FinikWalletError.None;
        }

        public static bool HasPeriodIncome(FinikWallet wallet, string periodId)
        {
            foreach (var t in wallet.transactions)
                if (t.kind == FinikTransactionKind.Earn && t.source == FinikTransactionSource.PeriodIncome && t.periodId == periodId)
                    return true;
            return false;
        }

        /// <summary>
        /// Repairs a wallet loaded from disk: balances are rebuilt from the transaction log when there is
        /// one, otherwise clamped to non-negative values.
        /// </summary>
        public static FinikWallet Normalize(FinikWallet wallet)
        {
            if (wallet == null) return new FinikWallet();
            wallet.transactions ??= new List<FinikTransaction>();
            wallet.transactions.RemoveAll(t => t == null);
            wallet.InvalidateIndex();
            if (wallet.transactions.Count > 0)
            {
                int balance = 0, savings = 0;
                foreach (var t in wallet.transactions)
                {
                    t.delta = TransactionDelta(t.kind, t.amount);
                    balance += t.delta;
                    savings += SavingsDelta(t.kind, t.amount);
                }
                wallet.balance = balance;
                wallet.savingsBalance = savings;
            }
            else
            {
                wallet.balance = Math.Max(0, wallet.balance);
                wallet.savingsBalance = Math.Max(0, wallet.savingsBalance);
            }
            return wallet;
        }

        public static string KindLabel(FinikTransactionKind kind) => kind switch
        {
            FinikTransactionKind.Earn => "Получено",
            FinikTransactionKind.Save => "В накопления",
            FinikTransactionKind.Withdraw => "Из накоплений",
            FinikTransactionKind.Reserve => "В резерв",
            FinikTransactionKind.Allocate => "В бюджет недели",
            _ => "Потрачено"
        };
    }
}
