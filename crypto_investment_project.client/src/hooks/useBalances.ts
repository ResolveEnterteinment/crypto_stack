import { useState, useEffect } from 'react';
import * as balanceService from '../services/balance';
import { Asset } from '../types/assetTypes';
import { useAuth } from '../context/AuthContext'; // Assuming you have a user context for authentication'
import { Balance } from '../types/balanceTypes';

export const useBalances = () => {
    const [balances, setBalances] = useState<Balance[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const { user } = useAuth(); // Get the current user from context

  useEffect(() => {
    const fetchBalances = async () => {
      try {
        setIsLoading(true);
          const response = await balanceService.getBalances(user?.id!);
          setBalances(response);
      } catch (err: any) {
        setError(err.message);
      } finally {
        setIsLoading(false);
      }
    };

    fetchBalances();
  }, []);

  return { balances, isLoading, error };
};