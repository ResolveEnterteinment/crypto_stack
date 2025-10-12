/**
 * ANT DESIGN THEME CONFIGURATION
 * 
 * Apple-inspired minimalist design system
 * Philosophy: Less is More
 * 
 * Core Principles:
 * - Generous whitespace
 * - Subtle depth and shadows
 * - Clear visual hierarchy
 * - Sophisticated, muted colors
 * - Smooth, purposeful animations
 */

import type { ThemeConfig } from 'antd';

/**
 * Design Tokens - Apple-inspired palette
 */
const designTokens = {
  // Primary brand color - Sophisticated blue
  colorPrimary: '#007AFF', // Apple Blue
  
  // Semantic colors
  colorSuccess: '#34C759', // Apple Green
  colorWarning: '#FF9500', // Apple Orange
  colorError: '#FF3B30',   // Apple Red
  colorInfo: '#5AC8FA',    // Apple Cyan
  
  // Neutral palette - Sophisticated grays
  colorTextBase: '#1D1D1F',        // Almost black (primary text)
  colorTextSecondary: '#86868B',   // Medium gray (secondary text)
  colorTextTertiary: '#C7C7CC',    // Light gray (tertiary text)
  colorTextQuaternary: '#D1D1D6',  // Very light gray (disabled text)
  
  colorBgBase: '#FFFFFF',          // Pure white background
  colorBgContainer: '#F5F5F7',     // Light gray container
  colorBgElevated: '#FFFFFF',      // Elevated surfaces
  colorBgLayout: '#FAFAFA',        // Page background
  
  colorBorder: '#E5E5EA',          // Subtle borders
  colorBorderSecondary: '#F2F2F7', // Very subtle borders
  
  // Typography
  fontFamily: `-apple-system, BlinkMacSystemFont, 'SF Pro Display', 'SF Pro Text', 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif`,
  fontFamilyCode: `'SF Mono', Menlo, Monaco, Consolas, 'Courier New', monospace`,
  
  fontSize: 16,           // Base font size
  fontSizeHeading1: 48,   // H1
  fontSizeHeading2: 36,   // H2
  fontSizeHeading3: 28,   // H3
  fontSizeHeading4: 20,   // H4
  fontSizeHeading5: 16,   // H5
  
  lineHeight: 1.5,
  lineHeightHeading: 1.2,
  
  // Spacing - Generous and consistent
  marginXS: 8,
  marginSM: 12,
  margin: 16,
  marginMD: 20,
  marginLG: 24,
  marginXL: 32,
  marginXXL: 48,
  
  paddingXS: 8,
  paddingSM: 12,
  padding: 16,
  paddingMD: 20,
  paddingLG: 24,
  paddingXL: 32,
  paddingXXL: 48,
  
  // Border radius - Subtle and consistent
  borderRadius: 12,       // Standard radius
  borderRadiusLG: 16,     // Large radius
  borderRadiusSM: 8,      // Small radius
  borderRadiusXS: 4,      // Extra small radius
  
  // Shadows - Subtle depth (Apple-style)
  boxShadow: '0 1px 3px rgba(0, 0, 0, 0.04), 0 1px 2px rgba(0, 0, 0, 0.06)',
  boxShadowSecondary: '0 2px 8px rgba(0, 0, 0, 0.08), 0 1px 4px rgba(0, 0, 0, 0.04)',
  boxShadowTertiary: '0 4px 16px rgba(0, 0, 0, 0.08), 0 2px 8px rgba(0, 0, 0, 0.04)',
  
  // Control heights - Comfortable and accessible
  controlHeight: 44,      // Standard control (Apple's recommended touch target)
  controlHeightLG: 52,    // Large control
  controlHeightSM: 36,    // Small control
  
  // Motion - Smooth and purposeful
  motionDurationSlow: '0.3s',
  motionDurationMid: '0.2s',
  motionDurationFast: '0.15s',
  motionEaseInOut: 'cubic-bezier(0.4, 0, 0.2, 1)',
  motionEaseOut: 'cubic-bezier(0.0, 0, 0.2, 1)',
  motionEaseIn: 'cubic-bezier(0.4, 0, 1, 1)',
  
  // Other
  screenXS: 480,
  screenSM: 576,
  screenMD: 768,
  screenLG: 992,
  screenXL: 1200,
  screenXXL: 1600,
  
  wireframe: false,
  zIndexBase: 0,
  zIndexPopupBase: 1000,
};

/**
 * Component-specific customizations
 */
const componentTokens = {
  Button: {
    // Primary button
    primaryColor: '#FFFFFF',
    colorPrimary: designTokens.colorPrimary,
    colorPrimaryHover: '#0051D5',
    colorPrimaryActive: '#004FC4',
    
    // Default button
    defaultColor: designTokens.colorTextBase,
    defaultBg: '#FFFFFF',
    defaultBorderColor: designTokens.colorBorder,
    defaultHoverBg: '#F5F5F7',
    defaultHoverBorderColor: designTokens.colorBorder,
    defaultActiveBg: '#E8E8ED',
    defaultActiveBorderColor: designTokens.colorBorder,
    
    // Button sizing
    controlHeight: designTokens.controlHeight,
    controlHeightLG: designTokens.controlHeightLG,
    controlHeightSM: designTokens.controlHeightSM,
    
    // Button styling
    borderRadius: designTokens.borderRadius,
    borderRadiusLG: designTokens.borderRadiusLG,
    borderRadiusSM: designTokens.borderRadiusSM,
    
    fontWeight: 500,
    primaryShadow: '0 2px 8px rgba(0, 122, 255, 0.15)',
    dangerShadow: '0 2px 8px rgba(255, 59, 48, 0.15)',
    
    // Ghost button
    ghostBg: 'transparent',
    ghostColor: designTokens.colorPrimary,
  },
  
  Input: {
    controlHeight: designTokens.controlHeight,
    controlHeightLG: designTokens.controlHeightLG,
    controlHeightSM: designTokens.controlHeightSM,
    borderRadius: designTokens.borderRadius,
    colorBorder: designTokens.colorBorder,
    colorBgContainer: '#FAFAFA',
    paddingBlock: 12,
    paddingInline: 16,
    fontSize: 16,
    hoverBorderColor: designTokens.colorPrimary,
    activeBorderColor: designTokens.colorPrimary,
    activeShadow: `0 0 0 3px rgba(0, 122, 255, 0.08)`,
  },
  
  Select: {
    controlHeight: designTokens.controlHeight,
    borderRadius: designTokens.borderRadius,
    colorBorder: designTokens.colorBorder,
    optionSelectedBg: '#F5F5F7',
    optionActiveBg: '#F9F9FB',
    optionPadding: '12px 16px',
  },
  
  Card: {
    borderRadiusLG: designTokens.borderRadiusLG,
    boxShadowTertiary: '0 2px 12px rgba(0, 0, 0, 0.04), 0 1px 4px rgba(0, 0, 0, 0.02)',
    colorBorderSecondary: designTokens.colorBorderSecondary,
    paddingLG: 24,
    headerFontSize: 20,
    headerFontSizeSM: 16,
  },
  
  Modal: {
    borderRadiusLG: designTokens.borderRadiusLG,
    boxShadow: '0 12px 48px rgba(0, 0, 0, 0.12), 0 4px 16px rgba(0, 0, 0, 0.08)',
    headerBg: '#FFFFFF',
    contentBg: '#FFFFFF',
    titleFontSize: 24,
    titleLineHeight: 1.3,
  },
  
  Table: {
    borderRadius: designTokens.borderRadius,
    headerBg: '#F9F9FB',
    headerColor: designTokens.colorTextSecondary,
    rowHoverBg: '#F9F9FB',
    cellPaddingBlock: 16,
    cellPaddingInline: 16,
    fontSize: 15,
  },
  
  Tabs: {
    itemColor: designTokens.colorTextSecondary,
    itemSelectedColor: designTokens.colorPrimary,
    itemHoverColor: designTokens.colorTextBase,
    titleFontSize: 16,
    inkBarColor: designTokens.colorPrimary,
    cardBg: '#FFFFFF',
    cardGutter: 4,
  },
  
  Menu: {
    itemBg: 'transparent',
    itemSelectedBg: '#F5F5F7',
    itemHoverBg: '#F9F9FB',
    itemColor: designTokens.colorTextBase,
    itemSelectedColor: designTokens.colorPrimary,
    itemBorderRadius: 8,
    itemPaddingInline: 16,
    itemMarginInline: 4,
    fontSize: 15,
  },
  
  Notification: {
    width: 420,
    borderRadiusLG: designTokens.borderRadiusLG,
    boxShadow: '0 8px 32px rgba(0, 0, 0, 0.12), 0 2px 8px rgba(0, 0, 0, 0.06)',
  },
  
  Message: {
    contentBg: 'rgba(0, 0, 0, 0.85)',
    contentPadding: '12px 16px',
    borderRadiusLG: 10,
  },
  
  Tooltip: {
    colorBgSpotlight: 'rgba(0, 0, 0, 0.85)',
    borderRadius: 8,
    paddingSM: 8,
    paddingXS: 12,
  },
  
  Switch: {
    trackHeight: 32,
    trackMinWidth: 52,
    handleSize: 28,
    innerMinMargin: 4,
    innerMaxMargin: 24,
  },
  
  Slider: {
    trackBg: '#E5E5EA',
    trackHoverBg: '#D1D1D6',
    handleColor: '#FFFFFF',
    handleSize: 20,
    handleSizeHover: 24,
    railSize: 6,
    dotSize: 12,
  },
  
  Progress: {
    defaultColor: designTokens.colorPrimary,
    remainingColor: '#E5E5EA',
    lineBorderRadius: 100,
    circleTextFontSize: '1.2em',
  },
  
  Badge: {
    dotSize: 8,
    textFontSize: 12,
    textFontWeight: 500,
  },
  
  Tag: {
    defaultBg: '#F5F5F7',
    defaultColor: designTokens.colorTextBase,
    borderRadiusSM: 6,
    fontSizeSM: 13,
  },
  
  Alert: {
    borderRadiusLG: designTokens.borderRadius,
    withDescriptionPadding: '16px 20px',
    withDescriptionIconSize: 24,
  },
  
  Divider: {
    colorSplit: designTokens.colorBorderSecondary,
  },
  
  Skeleton: {
    gradientFromColor: 'rgba(0, 0, 0, 0.06)',
    gradientToColor: 'rgba(0, 0, 0, 0.02)',
    borderRadius: 8,
  },
};

/**
 * Main Theme Configuration
 */
export const antdTheme: ThemeConfig = {
  token: designTokens,
  components: componentTokens,
  
  // CSS Variables for easy access
  cssVar: true,
  
  // Hash priority for style injection
  hashed: true,
};

/**
 * Dark Mode Theme Configuration
 */
export const antdDarkTheme: ThemeConfig = {
  token: {
    ...designTokens,
    
    // Dark mode colors
    colorTextBase: '#F5F5F7',
    colorTextSecondary: '#98989D',
    colorTextTertiary: '#636366',
    colorTextQuaternary: '#48484A',
    
    colorBgBase: '#000000',
    colorBgContainer: '#1C1C1E',
    colorBgElevated: '#2C2C2E',
    colorBgLayout: '#000000',
    
    colorBorder: '#38383A',
    colorBorderSecondary: '#2C2C2E',
    
    // Adjust primary for dark mode
    colorPrimary: '#0A84FF', // Brighter blue for dark mode
    
    // Shadows for dark mode (more pronounced)
    boxShadow: '0 2px 8px rgba(0, 0, 0, 0.3), 0 1px 4px rgba(0, 0, 0, 0.2)',
    boxShadowSecondary: '0 4px 16px rgba(0, 0, 0, 0.4), 0 2px 8px rgba(0, 0, 0, 0.3)',
    boxShadowTertiary: '0 8px 32px rgba(0, 0, 0, 0.5), 0 4px 16px rgba(0, 0, 0, 0.4)',
  },
  
  components: {
    ...componentTokens,
    Button: {
      ...componentTokens.Button,
      defaultBg: '#1C1C1E',
      defaultHoverBg: '#2C2C2E',
      defaultActiveBg: '#3A3A3C',
    },
    Input: {
      ...componentTokens.Input,
      colorBgContainer: '#1C1C1E',
    },
    Card: {
      ...componentTokens.Card,
      boxShadowTertiary: '0 4px 16px rgba(0, 0, 0, 0.6), 0 2px 8px rgba(0, 0, 0, 0.4)',
    },
    Table: {
      ...componentTokens.Table,
      headerBg: '#1C1C1E',
      rowHoverBg: '#2C2C2E',
    },
    Menu: {
      ...componentTokens.Menu,
      itemSelectedBg: '#2C2C2E',
      itemHoverBg: '#1C1C1E',
    },
  },
  
  cssVar: true,
  hashed: true,
};

/**
 * Export theme presets
 */
export const themePresets = {
  light: antdTheme,
  dark: antdDarkTheme,
};

export default antdTheme;
