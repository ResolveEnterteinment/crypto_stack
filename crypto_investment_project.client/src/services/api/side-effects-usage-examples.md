# Preventing Front-End Side Effects - Implementation Guide

## Common Side Effects and Solutions

### 1. **Double Form Submissions**

#### Problem:
```typescript
// ❌ BAD: User can click submit multiple times
const handleSubmit = async (e) => {
  e.preventDefault();
  await api.post('/subscribe', formData);
};
```

#### Solution:
```typescript
// ✅ GOOD: Using SafeForm component
import { SafeForm } from '@/components/SafeForm';

const SubscriptionForm = () => {
  const handleSubmit = async (data: any) => {
    const response = await api.post('/subscriptions', data);
    toast.success('Subscription created!');
  };

  return (
    <SafeForm 
      onSubmit={handleSubmit}
      preventDoubleSubmit={true}
      debounceMs={300}
    >
      <input name="email" type="email" required />
      <input name="amount" type="number" required />
      <button type="submit">Subscribe</button>
    </SafeForm>
  );
};
```

### 2. **Duplicate API Requests**

#### Problem:
```typescript
// ❌ BAD: Multiple components fetching same data
const ComponentA = () => {
  useEffect(() => {
    api.get('/user/profile');
  }, []);
};

const ComponentB = () => {
  useEffect(() => {
    api.get('/user/profile'); // Duplicate request!
  }, []);
};
```

#### Solution:
```typescript
// ✅ GOOD: Using request deduplication
import { useRequestDeduplication } from '@/hooks/useRequestDeduplication';

const useUserProfile = () => {
  const { dedupedRequest } = useRequestDeduplication();
  
  const fetchProfile = () => {
    return dedupedRequest(
      'user-profile', // Dedup key
      () => api.get('/user/profile')
    );
  };

  return { fetchProfile };
};

// Both components will share the same request
const ComponentA = () => {
  const { fetchProfile } = useUserProfile();
  useEffect(() => { fetchProfile(); }, []);
};

const ComponentB = () => {
  const { fetchProfile } = useUserProfile();
  useEffect(() => { fetchProfile(); }, []);
};
```

### 3. **Search Input Flooding**

#### Problem:
```typescript
// ❌ BAD: API call on every keystroke
const SearchBox = () => {
  const [query, setQuery] = useState('');

  useEffect(() => {
    if (query) {
      api.get(`/search?q=${query}`); // Floods the server!
    }
  }, [query]);

  return <input onChange={(e) => setQuery(e.target.value)} />;
};
```

#### Solution:
```typescript
// ✅ GOOD: Using debounce
import { useDebounce, useDebouncedCallback } from '@/hooks/useDebounce';

const SearchBox = () => {
  const [query, setQuery] = useState('');
  const debouncedQuery = useDebounce(query, 500);

  useEffect(() => {
    if (debouncedQuery) {
      api.get(`/search?q=${debouncedQuery}`);
    }
  }, [debouncedQuery]);

  return <input onChange={(e) => setQuery(e.target.value)} />;
};

// Alternative with callback
const SearchBoxAlt = () => {
  const search = useDebouncedCallback(
    (query: string) => api.get(`/search?q=${query}`),
    500
  );

  return <input onChange={(e) => search(e.target.value)} />;
};
```

### 4. **Infinite Scroll Spam**

#### Problem:
```typescript
// ❌ BAD: Multiple scroll events trigger multiple loads
const InfiniteList = () => {
  const handleScroll = () => {
    if (isNearBottom()) {
      loadMore(); // Can be called many times rapidly!
    }
  };
};
```

#### Solution:
```typescript
// ✅ GOOD: Using throttle
import { useThrottledCallback } from '@/hooks/useThrottle';

const InfiniteList = () => {
  const [loading, setLoading] = useState(false);
  
  const loadMore = useThrottledCallback(async () => {
    if (loading) return;
    
    setLoading(true);
    await api.get('/items?page=' + nextPage);
    setLoading(false);
  }, 1000); // Max once per second

  useEffect(() => {
    window.addEventListener('scroll', loadMore);
    return () => window.removeEventListener('scroll', loadMore);
  }, [loadMore]);
};
```

### 5. **Component Unmount Requests**

#### Problem:
```typescript
// ❌ BAD: Updates state after unmount
const DataComponent = () => {
  const [data, setData] = useState(null);

  useEffect(() => {
    api.get('/data').then(setData); // Can set state after unmount!
  }, []);
};
```

#### Solution:
```typescript
// ✅ GOOD: Using safe async
import { useSafeAsync } from '@/hooks/useSafeAsync';

const DataComponent = () => {
  const { data, loading, error, execute } = useSafeAsync();

  useEffect(() => {
    execute((signal) => 
      api.get('/data', { signal })
    );
  }, []);

  if (loading) return <Spinner />;
  if (error) return <Error />;
  return <div>{data}</div>;
};
```

### 6. **Race Conditions**

#### Problem:
```typescript
// ❌ BAD: Search results arrive out of order
const Search = () => {
  const [results, setResults] = useState([]);

  const search = async (query: string) => {
    const data = await api.get(`/search?q=${query}`);
    setResults(data); // Old request might resolve after new one!
  };
};
```

#### Solution:
```typescript
// ✅ GOOD: Cancel previous requests
const Search = () => {
  const abortControllerRef = useRef<AbortController>();
  const [results, setResults] = useState([]);

  const search = async (query: string) => {
    // Cancel previous request
    if (abortControllerRef.current) {
      abortControllerRef.current.abort();
    }

    // Create new controller
    abortControllerRef.current = new AbortController();

    try {
      const data = await api.get(`/search?q=${query}`, {
        signal: abortControllerRef.current.signal
      });
      setResults(data);
    } catch (error) {
      if (error.name !== 'AbortError') {
        console.error(error);
      }
    }
  };

  return <input onChange={(e) => search(e.target.value)} />;
};
```

### 7. **useEffect Double Execution (React 18 StrictMode)**

#### Problem:
```typescript
// ❌ BAD: Runs twice in development
useEffect(() => {
  api.post('/analytics/track', { event: 'page_view' }); // Sent twice!
}, []);
```

#### Solution:
```typescript
// ✅ GOOD: Using useEffectOnce
import { useEffectOnce } from '@/hooks/useEffectOnce';

const Analytics = () => {
  useEffectOnce(() => {
    api.post('/analytics/track', { event: 'page_view' });
  });
};

// Alternative: Using cleanup properly
useEffect(() => {
  let cancelled = false;

  const track = async () => {
    if (!cancelled) {
      await api.post('/analytics/track', { event: 'page_view' });
    }
  };

  track();

  return () => {
    cancelled = true;
  };
}, []);
```

## Complete Component Example

```typescript
import React, { useState } from 'react';
import { 
  useApiCall, 
  useDebounce, 
  useRequestDeduplication,
  useSafeAsync 
} from '@/hooks';
import { SafeForm, SafeButton } from '@/components';

const SubscriptionManager: React.FC = () => {
  const [searchTerm, setSearchTerm] = useState('');
  const debouncedSearch = useDebounce(searchTerm, 500);
  const { dedupedRequest } = useRequestDeduplication();

  // Fetch subscriptions with deduplication
  const {
    data: subscriptions,
    loading,
    execute: fetchSubscriptions
  } = useApiCall('/subscriptions', 'get', {
    dedupe: true,
    onError: (error) => toast.error('Failed to load subscriptions')
  });

  // Search with debounce
  const {
    data: searchResults,
    execute: search
  } = useApiCall('/subscriptions/search', 'get', {
    debounceMs: 500
  });

  // Create subscription with idempotency
  const createSubscription = useApiCall('/subscriptions', 'post', {
    dedupeKey: `create-sub-${Date.now()}`,
    onSuccess: () => {
      toast.success('Subscription created!');
      fetchSubscriptions();
    }
  });

  // Update with throttle (for slider/drag scenarios)
  const updateAmount = useApiCall('/subscriptions/amount', 'put', {
    throttleMs: 1000,
    dedupe: true
  });

  useEffect(() => {
    fetchSubscriptions();
  }, []);

  useEffect(() => {
    if (debouncedSearch) {
      search({ q: debouncedSearch });
    }
  }, [debouncedSearch]);

  return (
    <div>
      {/* Search with debounce */}
      <input
        type="text"
        placeholder="Search subscriptions..."
        value={searchTerm}
        onChange={(e) => setSearchTerm(e.target.value)}
      />

      {/* Form with double-submit prevention */}
      <SafeForm
        onSubmit={async (data) => {
          await createSubscription.execute(data);
        }}
        preventDoubleSubmit={true}
      >
        <input name="name" placeholder="Subscription name" required />
        <input name="amount" type="number" required />
        
        <SafeButton type="submit" minClickInterval={1000}>
          Create Subscription
        </SafeButton>
      </SafeForm>

      {/* List with safe delete */}
      {subscriptions?.map(sub => (
        <div key={sub.id}>
          <span>{sub.name}</span>
          <SafeButton
            onClick={async () => {
              await dedupedRequest(
                `delete-${sub.id}`,
                () => api.delete(`/subscriptions/${sub.id}`)
              );
              fetchSubscriptions();
            }}
            minClickInterval={2000}
          >
            Delete
          </SafeButton>
        </div>
      ))}
    </div>
  );
};
```

## Global Request Queue Manager

```typescript
// src/services/RequestQueue.ts
class RequestQueueManager {
  private queue: Map<string, Promise<any>> = new Map();
  private locks: Map<string, boolean> = new Map();

  async enqueue<T>(
    key: string,
    requestFn: () => Promise<T>,
    options: { 
      dedupe?: boolean; 
      maxConcurrent?: number;
      priority?: number;
    } = {}
  ): Promise<T> {
    const { dedupe = true, maxConcurrent = 5 } = options;

    // Check for existing request
    if (dedupe && this.queue.has(key)) {
      console.log(`[Queue] Returning existing request: ${key}`);
      return this.queue.get(key);
    }

    // Wait if too many concurrent requests
    while (this.queue.size >= maxConcurrent) {
      await new Promise(resolve => setTimeout(resolve, 100));
    }

    // Execute request
    const promise = requestFn().finally(() => {
      this.queue.delete(key);
      this.locks.delete(key);
    });

    this.queue.set(key, promise);
    return promise;
  }

  cancel(key: string) {
    this.queue.delete(key);
    this.locks.delete(key);
  }

  clear() {
    this.queue.clear();
    this.locks.clear();
  }

  get size() {
    return this.queue.size;
  }
}

export const requestQueue = new RequestQueueManager();

// Usage
const fetchUserData = () => {
  return requestQueue.enqueue(
    'user-data',
    () => api.get('/user'),
    { dedupe: true, priority: 1 }
  );
};
```

## Best Practices Summary

### ✅ DO:
1. **Debounce** user input (search, autocomplete)
2. **Throttle** scroll/resize events
3. **Deduplicate** identical requests
4. **Cancel** requests on unmount
5. **Disable** forms during submission
6. **Use idempotency keys** for critical operations
7. **Implement retry logic** with exponential backoff
8. **Queue requests** to prevent overload

### ❌ DON'T:
1. Make API calls in render functions
2. Update state after component unmount
3. Allow rapid button clicks
4. Send requests on every keystroke
5. Ignore abort signals
6. Forget cleanup in useEffect
7. Make synchronous blocking calls
8. Trust client-side validation alone

## Monitoring Side Effects

```typescript
// src/utils/monitoring.ts
export const SideEffectMonitor = {
  track(event: string, data?: any) {
    if (process.env.NODE_ENV === 'development') {
      console.log(`[Side Effect] ${event}`, data);
    }
    
    // Send to analytics in production
    if (window.analytics) {
      window.analytics.track(event, data);
    }
  },

  reportDuplicate(endpoint: string) {
    this.track('duplicate_request', { endpoint });
  },

  reportTimeout(endpoint: string, duration: number) {
    this.track('request_timeout', { endpoint, duration });
  },

  reportRapidFire(action: string, count: number) {
    this.track('rapid_fire_prevented', { action, count });
  }
};
```

## Testing Side Effect Prevention

```typescript
// __tests__/sideEffects.test.ts
import { renderHook, act, waitFor } from '@testing-library/react';
import { useRequestDeduplication } from '@/hooks/useRequestDeduplication';

describe('Side Effect Prevention', () => {
  it('should deduplicate concurrent requests', async () => {
    const { result } = renderHook(() => useRequestDeduplication());
    const mockRequest = jest.fn().mockResolvedValue('data');

    // Fire multiple requests with same key
    const promises = await act(async () => {
      return Promise.all([
        result.current.dedupedRequest('key1', mockRequest),
        result.current.dedupedRequest('key1', mockRequest),
        result.current.dedupedRequest('key1', mockRequest)
      ]);
    });

    // Should only call the actual request once
    expect(mockRequest).toHaveBeenCalledTimes(1);
    
    // All promises should resolve to same value
    expect(promises[0]).toBe(promises[1]);
    expect(promises[1]).toBe(promises[2]);
  });

  it('should prevent double form submission', async () => {
    const onSubmit = jest.fn();
    const { getByRole } = render(
      <SafeForm onSubmit={onSubmit} preventDoubleSubmit>
        <button type="submit">Submit</button>
      </SafeForm>
    );

    const button = getByRole('button');
    
    // Click multiple times rapidly
    fireEvent.click(button);
    fireEvent.click(button);
    fireEvent.click(button);

    await waitFor(() => {
      expect(onSubmit).toHaveBeenCalledTimes(1);
    });
  });
});
```