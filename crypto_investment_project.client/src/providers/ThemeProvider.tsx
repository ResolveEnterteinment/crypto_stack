/**
 * THEME PROVIDER COMPONENT
 * 
 * Wraps the application with Ant Design ConfigProvider
 * and provides theme switching capabilities (light/dark mode)
 * 
 * Features:
 * - Ant Design theme configuration
 * - Dark mode support
 * - System preference detection
 * - Persistent theme preference
 * - Smooth theme transitions
 */

import React, { createContext, useContext, useEffect, useState, useCallback } from 'react';
import { ConfigProvider, theme as antdTheme } from 'antd';
import { antdTheme as lightTheme, antdDarkTheme as darkTheme } from '../config/antd-theme.config';

/* ========================================
   TYPE DEFINITIONS
   ======================================== */

type ThemeMode = 'light' | 'dark' | 'system';

interface ThemeContextValue {
  /** Current theme mode */
  mode: ThemeMode;
  /** Actual theme being used (resolved from 'system' if needed) */
  activeTheme: 'light' | 'dark';
  /** Set the theme mode */
  setMode: (mode: ThemeMode) => void;
  /** Toggle between light and dark */
  toggleTheme: () => void;
}

/* ========================================
   THEME CONTEXT
   ======================================== */

const ThemeContext = createContext<ThemeContextValue | undefined>(undefined);

/**
 * Hook to access theme context
 */
export const useTheme = (): ThemeContextValue => {
  const context = useContext(ThemeContext);
  if (!context) {
    throw new Error('useTheme must be used within ThemeProvider');
  }
  return context;
};

/* ========================================
   THEME PROVIDER COMPONENT
   ======================================== */

interface ThemeProviderProps {
  children: React.ReactNode;
  /** Default theme mode (defaults to 'system') */
  defaultMode?: ThemeMode;
  /** Storage key for persisting theme preference */
  storageKey?: string;
}

export const ThemeProvider: React.FC<ThemeProviderProps> = ({
  children,
  defaultMode = 'system',
  storageKey = 'theme-mode',
}) => {
  // State for theme mode
  const [mode, setModeState] = useState<ThemeMode>(() => {
    // Try to get saved preference from localStorage
    if (typeof window !== 'undefined') {
      try {
        const saved = localStorage.getItem(storageKey);
        if (saved === 'light' || saved === 'dark' || saved === 'system') {
          return saved;
        }
      } catch (error) {
        console.warn('Failed to read theme preference from localStorage:', error);
      }
    }
    return defaultMode;
  });

  // Get system preference
  const [systemPreference, setSystemPreference] = useState<'light' | 'dark'>(() => {
    if (typeof window !== 'undefined' && window.matchMedia) {
      return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
    }
    return 'light';
  });

  // Determine active theme
  const activeTheme = mode === 'system' ? systemPreference : mode;

  /**
   * Update theme mode and persist to localStorage
   */
  const setMode = useCallback((newMode: ThemeMode) => {
    setModeState(newMode);
    
    // Persist to localStorage
    if (typeof window !== 'undefined') {
      try {
        localStorage.setItem(storageKey, newMode);
      } catch (error) {
        console.warn('Failed to save theme preference to localStorage:', error);
      }
    }

    // Update data attribute for CSS
    document.documentElement.setAttribute('data-theme', newMode === 'system' ? systemPreference : newMode);
    
    // Add/remove dark class for utility CSS
    if (newMode === 'dark' || (newMode === 'system' && systemPreference === 'dark')) {
      document.documentElement.classList.add('dark');
    } else {
      document.documentElement.classList.remove('dark');
    }
  }, [storageKey, systemPreference]);

  /**
   * Toggle between light and dark
   */
  const toggleTheme = useCallback(() => {
    setMode(activeTheme === 'light' ? 'dark' : 'light');
  }, [activeTheme, setMode]);

  /**
   * Listen to system preference changes
   */
  useEffect(() => {
    if (typeof window === 'undefined' || !window.matchMedia) {
      return;
    }

    const mediaQuery = window.matchMedia('(prefers-color-scheme: dark)');
    
    const handleChange = (e: MediaQueryListEvent | MediaQueryList) => {
      const newPreference = e.matches ? 'dark' : 'light';
      setSystemPreference(newPreference);
      
      // Update document if using system preference
      if (mode === 'system') {
        document.documentElement.setAttribute('data-theme', newPreference);
        if (newPreference === 'dark') {
          document.documentElement.classList.add('dark');
        } else {
          document.documentElement.classList.remove('dark');
        }
      }
    };

    // Modern browsers
    if (mediaQuery.addEventListener) {
      mediaQuery.addEventListener('change', handleChange);
      return () => mediaQuery.removeEventListener('change', handleChange);
    } 
    // Older browsers
    else if (mediaQuery.addListener) {
      mediaQuery.addListener(handleChange);
      return () => mediaQuery.removeListener(handleChange);
    }
  }, [mode]);

  /**
   * Initialize theme on mount
   */
  useEffect(() => {
    // Set initial data attribute
    document.documentElement.setAttribute('data-theme', activeTheme);
    
    // Add/remove dark class
    if (activeTheme === 'dark') {
      document.documentElement.classList.add('dark');
    } else {
      document.documentElement.classList.remove('dark');
    }
  }, [activeTheme]);

  // Context value
  const value: ThemeContextValue = {
    mode,
    activeTheme,
    setMode,
    toggleTheme,
  };

  // Select appropriate Ant Design theme
  const currentAntdTheme = activeTheme === 'dark' ? darkTheme : lightTheme;

  return (
    <ThemeContext.Provider value={value}>
      <ConfigProvider
        theme={currentAntdTheme}
        // Ant Design algorithm for dark mode
        // algorithm={activeTheme === 'dark' ? antdTheme.darkAlgorithm : antdTheme.defaultAlgorithm}
      >
        {/* Smooth transition overlay when theme changes */}
        <style>{`
          * {
            transition: background-color 200ms cubic-bezier(0.4, 0, 0.2, 1),
                        color 200ms cubic-bezier(0.4, 0, 0.2, 1),
                        border-color 200ms cubic-bezier(0.4, 0, 0.2, 1);
          }
          
          /* Disable transitions on first load */
          .disable-transitions * {
            transition: none !important;
          }
        `}</style>
        {children}
      </ConfigProvider>
    </ThemeContext.Provider>
  );
};

/* ========================================
   THEME TOGGLE BUTTON COMPONENT
   ======================================== */

interface ThemeToggleProps {
  /** Custom class name */
  className?: string;
  /** Show label text */
  showLabel?: boolean;
}

/**
 * Ready-to-use theme toggle button
 */
export const ThemeToggle: React.FC<ThemeToggleProps> = ({ 
  className = '', 
  showLabel = false 
}) => {
  const { activeTheme, toggleTheme } = useTheme();

  return (
    <button
      onClick={toggleTheme}
      className={`theme-toggle ${className}`}
      aria-label={`Switch to ${activeTheme === 'light' ? 'dark' : 'light'} mode`}
      title={`Switch to ${activeTheme === 'light' ? 'dark' : 'light'} mode`}
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: '8px',
        padding: '8px 12px',
        borderRadius: '8px',
        background: activeTheme === 'light' ? '#F5F5F7' : '#2C2C2E',
        color: activeTheme === 'light' ? '#1D1D1F' : '#F5F5F7',
        border: 'none',
        cursor: 'pointer',
        transition: 'all 200ms cubic-bezier(0.4, 0, 0.2, 1)',
      }}
    >
      {/* Sun Icon (Light Mode) */}
      {activeTheme === 'light' ? (
        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
          <circle cx="12" cy="12" r="5"/>
          <line x1="12" y1="1" x2="12" y2="3"/>
          <line x1="12" y1="21" x2="12" y2="23"/>
          <line x1="4.22" y1="4.22" x2="5.64" y2="5.64"/>
          <line x1="18.36" y1="18.36" x2="19.78" y2="19.78"/>
          <line x1="1" y1="12" x2="3" y2="12"/>
          <line x1="21" y1="12" x2="23" y2="12"/>
          <line x1="4.22" y1="19.78" x2="5.64" y2="18.36"/>
          <line x1="18.36" y1="5.64" x2="19.78" y2="4.22"/>
        </svg>
      ) : (
        /* Moon Icon (Dark Mode) */
        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
          <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z"/>
        </svg>
      )}
      {showLabel && <span>{activeTheme === 'light' ? 'Light' : 'Dark'}</span>}
    </button>
  );
};

export default ThemeProvider;
