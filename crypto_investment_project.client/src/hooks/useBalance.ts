import { useState, useEffect } from 'react';
import { Balance } from '../types/balanceTypes';
import * as balanceService from '../services/balance';
import withdrawalService from '../services/withdrawalService';


export const useBalance = (userId: string, assetTicker: string) => {
  const [balance, setBalance] = useState<Balance | null>(null);
  const [pending, setPending] = useState<number | 0>(0);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

    const fetchBalance = async () => {
        console.log(`Fetching ${assetTicker} balance...`);
    if (!userId || !assetTicker) {
        setBalance(null);
      return;
    }

    try {
      setIsLoading(true);
        const balanceResponse = await balanceService.getBalance(assetTicker);
        console.log(`${assetTicker} balance: ${balanceResponse}`);
        setBalance(balanceResponse);
        const pendingResponse = await withdrawalService.getPendingTotals(assetTicker);
        setPending(pendingResponse);
    } catch (err: any) {
        console.error(err.message);
      setError(err.message);
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    fetchBalance();
  }, [userId, assetTicker]);

    return { balance, pending, isLoading, error, refetch: fetchBalance };
};