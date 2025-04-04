// src/components/LandingPage/FaqAccordion.tsx
import React, { useState } from 'react';

interface FaqItem {
    question: string;
    answer: string | React.ReactNode;
    category?: string;
    icon?: React.ReactNode;
}

interface FaqAccordionProps {
    items: FaqItem[];
    showCategories?: boolean;
    maxItems?: number;
    className?: string;
    darkMode?: boolean;
}

const FaqAccordion: React.FC<FaqAccordionProps> = ({
    items,
    showCategories = false,
    maxItems = items.length,
    className = '',
    darkMode = false,
}) => {
    const [openIndex, setOpenIndex] = useState<number | null>(0);
    const [displayCount, setDisplayCount] = useState(maxItems);
    const [activeCategory, setActiveCategory] = useState<string | null>(null);

    // Get unique categories if needed
    const categories = showCategories
        ? Array.from(new Set(items.map(item => item.category || 'General'))).sort()
        : [];

    // Filter items by category if one is selected
    const filteredItems = activeCategory
        ? items.filter(item => (item.category || 'General') === activeCategory)
        : items;

    // Toggle an item open/closed
    const toggleItem = (index: number) => {
        setOpenIndex(openIndex === index ? null : index);
    };

    // Load more items
    const loadMore = () => {
        setDisplayCount(prev => Math.min(prev + 5, filteredItems.length));
    };

    // Default FAQ icon
    const defaultIcon = (
        <svg className="w-5 h-5 text-blue-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M8.228 9c.549-1.165 2.03-2 3.772-2 2.21 0 4 1.343 4 3 0 1.4-1.278 2.575-3.006 2.907-.542.104-.994.54-.994 1.093m0 3h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
        </svg>
    );

    // Base colors based on theme
    const baseStyles = {
        background: darkMode ? 'bg-gray-900' : 'bg-white',
        text: darkMode ? 'text-white' : 'text-gray-900',
        secondary: darkMode ? 'text-gray-400' : 'text-gray-600',
        border: darkMode ? 'border-gray-700' : 'border-gray-200',
        hover: darkMode ? 'hover:bg-gray-800' : 'hover:bg-gray-50',
        active: darkMode ? 'bg-gray-800' : 'bg-gray-50',
    };

    return (
        <div className={`faq-accordion ${baseStyles.background} ${baseStyles.text} rounded-xl ${className}`}>
            {/* Category tabs */}
            {showCategories && categories.length > 1 && (
                <div className={`flex flex-wrap gap-2 mb-6 p-4 ${baseStyles.border} border-b`}>
                    <button
                        className={`px-4 py-2 rounded-full text-sm font-medium transition-colors ${activeCategory === null
                                ? `bg-blue-500 text-white`
                                : `${baseStyles.hover} ${baseStyles.secondary}`
                            }`}
                        onClick={() => setActiveCategory(null)}
                    >
                        All
                    </button>

                    {categories.map(category => (
                        <button
                            key={category}
                            className={`px-4 py-2 rounded-full text-sm font-medium transition-colors ${activeCategory === category
                                    ? `bg-blue-500 text-white`
                                    : `${baseStyles.hover} ${baseStyles.secondary}`
                                }`}
                            onClick={() => setActiveCategory(category)}
                        >
                            {category}
                        </button>
                    ))}
                </div>
            )}

            {/* FAQ items */}
            <div className="divide-y divide-dashed divide-gray-200">
                {filteredItems.slice(0, displayCount).map((item, index) => (
                    <div
                        key={index}
                        className={`transition-all duration-300 ${openIndex === index ? `${baseStyles.active}` : ''}`}
                    >
                        <button
                            className={`w-full flex justify-between items-center p-5 text-left ${baseStyles.hover} transition-colors focus:outline-none`}
                            onClick={() => toggleItem(index)}
                            aria-expanded={openIndex === index}
                        >
                            <div className="flex items-center">
                                <div className="flex-shrink-0 mr-4">
                                    {item.icon || defaultIcon}
                                </div>
                                <span className="text-lg font-medium">{item.question}</span>
                            </div>
                            <svg
                                className={`flex-shrink-0 w-6 h-6 transition-transform duration-300 ${openIndex === index ? 'transform rotate-180' : ''
                                    } ${baseStyles.secondary}`}
                                fill="none"
                                stroke="currentColor"
                                viewBox="0 0 24 24"
                            >
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M19 9l-7 7-7-7" />
                            </svg>
                        </button>

                        {/* Answer panel */}
                        <div
                            className={`overflow-hidden transition-all duration-300 ${openIndex === index ? 'max-h-96' : 'max-h-0'
                                }`}
                        >
                            <div className={`p-5 pt-0 ${baseStyles.secondary}`}>
                                {typeof item.answer === 'string' ? (
                                    <p>{item.answer}</p>
                                ) : (
                                    item.answer
                                )}
                            </div>
                        </div>
                    </div>
                ))}
            </div>

            {/* Load more button */}
            {filteredItems.length > displayCount && (
                <div className="flex justify-center py-6">
                    <button
                        onClick={loadMore}
                        className={`px-6 py-2 rounded-lg ${darkMode
                                ? 'bg-gray-800 hover:bg-gray-700 text-white'
                                : 'bg-gray-100 hover:bg-gray-200 text-gray-800'
                            } transition-colors focus:outline-none focus:ring-2 focus:ring-blue-500`}
                    >
                        Load More
                        <svg
                            className="inline-block ml-2 w-4 h-4"
                            fill="none"
                            stroke="currentColor"
                            viewBox="0 0 24 24"
                        >
                            <path
                                strokeLinecap="round"
                                strokeLinejoin="round"
                                strokeWidth="2"
                                d="M19 9l-7 7-7-7"
                            />
                        </svg>
                    </button>
                </div>
            )}

            {/* Empty state */}
            {filteredItems.length === 0 && (
                <div className="flex flex-col items-center justify-center py-12">
                    <svg
                        className={`w-16 h-16 ${baseStyles.secondary} mb-4`}
                        fill="none"
                        stroke="currentColor"
                        viewBox="0 0 24 24"
                    >
                        <path
                            strokeLinecap="round"
                            strokeLinejoin="round"
                            strokeWidth="1"
                            d="M9.172 16.172a4 4 0 015.656 0M9 10h.01M15 10h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"
                        />
                    </svg>
                    <p className={`text-lg ${baseStyles.secondary}`}>No questions found in this category</p>
                    <button
                        onClick={() => setActiveCategory(null)}
                        className="mt-4 px-4 py-2 text-blue-500 hover:underline"
                    >
                        View all questions
                    </button>
                </div>
            )}
        </div>
    );
};

export default FaqAccordion;