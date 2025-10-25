using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.DTOs.Transaction
{
    /// <summary>
    /// Specifies which part of the balance is affected
    /// </summary>
    public enum BalanceType
    {
        /// <summary>
        /// Only the available balance is affected
        /// </summary>
        Available,

        /// <summary>
        /// Only the locked balance is affected
        /// </summary>
        Locked,

        /// <summary>
        /// Move from locked to available (unlock)
        /// </summary>
        UnlockToAvailable,

        /// <summary>
        /// Move from available to locked (lock)
        /// </summary>
        LockFromAvailable
    }
}
