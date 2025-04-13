// src/components/LandingPage/TestimonialCarousel.tsx
import React, { useState, useEffect, useRef } from 'react';
import Placeholder from './ui/Placeholder';

interface Testimonial {
    quote: string;
    name: string;
    title: string;
    image: string;
    companyLogo?: string;
    rating?: number;
}

interface TestimonialCarouselProps {
    testimonials: Testimonial[];
    autoplaySpeed?: number;
    className?: string;
    darkMode?: boolean;
}

const TestimonialCarousel: React.FC<TestimonialCarouselProps> = ({
    testimonials,
    autoplaySpeed = 5000,
    className = '',
    darkMode = false
}) => {
    const [activeIndex, setActiveIndex] = useState(0);
    const [isHovered, setIsHovered] = useState(false);
    const [isAnimating, setIsAnimating] = useState(false);
    const [direction, setDirection] = useState<'left' | 'right'>('right');
    const timerRef = useRef<number | null>(null);

    // Clean up timer on unmount
    useEffect(() => {
        return () => {
            if (timerRef.current) {
                window.clearTimeout(timerRef.current);
            }
        };
    }, []);

    // Set up autoplay
    useEffect(() => {
        if (isHovered) return;

        timerRef.current = window.setTimeout(() => {
            goToNext();
        }, autoplaySpeed);

        return () => {
            if (timerRef.current) {
                window.clearTimeout(timerRef.current);
            }
        };
    }, [activeIndex, isHovered, autoplaySpeed]);

    const goToPrevious = () => {
        if (isAnimating) return;

        setDirection('left');
        setIsAnimating(true);
        setActiveIndex((current) => (current === 0 ? testimonials.length - 1 : current - 1));

        // Reset animation state after transition
        setTimeout(() => setIsAnimating(false), 500);
    };

    const goToNext = () => {
        if (isAnimating) return;

        setDirection('right');
        setIsAnimating(true);
        setActiveIndex((current) => (current === testimonials.length - 1 ? 0 : current + 1));

        // Reset animation state after transition
        setTimeout(() => setIsAnimating(false), 500);
    };

    // Generate star rating elements
    const renderStars = (rating: number = 5) => {
        return Array.from({ length: 5 }).map((_, i) => (
            <svg
                key={i}
                className={`w-5 h-5 ${i < rating ? 'text-yellow-500' : 'text-gray-300'}`}
                fill="currentColor"
                viewBox="0 0 20 20"
            >
                <path d="M9.049 2.927c.3-.921 1.603-.921 1.902 0l1.07 3.292a1 1 0 00.95.69h3.462c.969 0 1.371 1.24.588 1.81l-2.8 2.034a1 1 0 00-.364 1.118l1.07 3.292c.3.921-.755 1.688-1.54 1.118l-2.8-2.034a1 1 0 00-1.175 0l-2.8 2.034c-.784.57-1.838-.197-1.539-1.118l1.07-3.292a1 1 0 00-.364-1.118L2.98 8.72c-.783-.57-.38-1.81.588-1.81h3.461a1 1 0 00.951-.69l1.07-3.292z"></path>
            </svg>
        ));
    };

    const currentTestimonial = testimonials[activeIndex];

    return (
        <div
            className={`testimonial-carousel relative overflow-hidden ${darkMode ? 'bg-gray-900 text-white' : 'bg-white text-gray-900'
                } rounded-xl shadow-lg ${className}`}
            onMouseEnter={() => setIsHovered(true)}
            onMouseLeave={() => setIsHovered(false)}
        >
            {/* Progress bar */}
            <div className="absolute top-0 left-0 w-full h-1 bg-gray-200">
                <div
                    className="h-full bg-blue-600 transition-all ease-linear"
                    style={{
                        width: `${isHovered ? 0 : 100}%`,
                        transitionDuration: `${isHovered ? '0s' : autoplaySpeed + 'ms'}`
                    }}
                />
            </div>

            <div className="relative p-6 md:p-8">
                {/* Quote icon */}
                <div className={`absolute top-6 right-6 w-16 h-16 opacity-10 ${darkMode ? 'text-white' : 'text-gray-900'}`}>
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1">
                        <path d="M9.5,5.5C8.3,5.5 7.4,5.8 6.6,6.3C5.8,6.9 5.2,7.6 4.7,8.5C4.2,9.4 3.9,10.3 3.7,11.3C3.5,12.3 3.5,13.3 3.5,14.3C3.5,15.8 3.9,17.2 4.7,18.3C5.5,19.4 6.7,20 8.3,20C9.4,20 10.4,19.6 11.2,18.7C12,17.9 12.4,16.8 12.4,15.6C12.4,14.4 12,13.4 11.2,12.6C10.4,11.8 9.4,11.4 8.3,11.4C8.1,11.4 7.9,11.4 7.7,11.5C7.5,11.6 7.3,11.6 7.1,11.7C7.2,10.9 7.5,10.1 8,9.3C8.5,8.5 9.2,7.9 10.2,7.4L9.5,5.5ZM20.5,5.5C19.3,5.5 18.4,5.8 17.6,6.3C16.8,6.9 16.2,7.6 15.7,8.5C15.2,9.4 14.9,10.3 14.7,11.3C14.5,12.3 14.5,13.3 14.5,14.3C14.5,15.8 14.9,17.2 15.7,18.3C16.5,19.4 17.7,20 19.3,20C20.4,20 21.4,19.6 22.2,18.7C23,17.9 23.4,16.8 23.4,15.6C23.4,14.4 23,13.4 22.2,12.6C21.4,11.8 20.4,11.4 19.3,11.4C19.1,11.4 18.9,11.4 18.7,11.5C18.5,11.6 18.3,11.6 18.1,11.7C18.2,10.9 18.5,10.1 19,9.3C19.5,8.5 20.2,7.9 21.2,7.4L20.5,5.5Z" />
                    </svg>
                </div>

                {/* Content area */}
                <div className="relative z-10">
                    {/* Rating stars */}
                    <div className="flex mb-4">
                        {renderStars(currentTestimonial.rating)}
                    </div>

                    {/* Quote text */}
                    <div
                        className="text-lg md:text-xl leading-relaxed mb-8 min-h-[120px]"
                        style={{
                            animation: `${direction === 'right' ? 'slideFromRight' : 'slideFromLeft'} 0.5s ease-out forwards`
                        }}
                    >
                        "{currentTestimonial.quote}"
                    </div>

                    {/* Author info */}
                    <div className="flex items-center">
                        <Placeholder
                            width={64}
                            height={64}
                            type="user"
                            className="w-14 h-14 rounded-full object-cover mr-4 border-2 border-gray-200"
                        />

                        <div>
                            <h4 className="font-bold text-lg">{currentTestimonial.name}</h4>
                            <p className={`${darkMode ? 'text-gray-400' : 'text-gray-600'}`}>{currentTestimonial.title}</p>
                        </div>
                        {currentTestimonial.companyLogo && (
                            <img
                                src={currentTestimonial.companyLogo}
                                alt="Company"
                                className="h-8 ml-auto"
                            />
                            
                        )}
                    </div>
                </div>
            </div>

            {/* Navigation buttons */}
            <div className="absolute bottom-6 right-6 flex space-x-2">
                <button
                    onClick={goToPrevious}
                    className={`p-2 rounded-full ${darkMode
                            ? 'bg-gray-800 hover:bg-gray-700 text-white'
                            : 'bg-gray-100 hover:bg-gray-200 text-gray-600'
                        } transition-colors focus:outline-none focus:ring-2 focus:ring-blue-500`}
                    aria-label="Previous testimonial"
                >
                    <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M15 19l-7-7 7-7" />
                    </svg>
                </button>
                <button
                    onClick={goToNext}
                    className={`p-2 rounded-full ${darkMode
                            ? 'bg-gray-800 hover:bg-gray-700 text-white'
                            : 'bg-gray-100 hover:bg-gray-200 text-gray-600'
                        } transition-colors focus:outline-none focus:ring-2 focus:ring-blue-500`}
                    aria-label="Next testimonial"
                >
                    <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M9 5l7 7-7 7" />
                    </svg>
                </button>
            </div>

            {/* Dots indicator */}
            <div className="absolute bottom-6 left-0 w-full flex justify-center space-x-2">
                {testimonials.map((_, index) => (
                    <button
                        key={index}
                        onClick={() => {
                            setDirection(index > activeIndex ? 'right' : 'left');
                            setActiveIndex(index);
                        }}
                        className={`w-2 h-2 rounded-full transition-all ${index === activeIndex
                                ? (darkMode ? 'bg-white w-4' : 'bg-blue-600 w-4')
                                : (darkMode ? 'bg-gray-600' : 'bg-gray-300')
                            }`}
                        aria-label={`Go to testimonial ${index + 1}`}
                    />
                ))}
            </div>

            {/* Keyframe animations */}
            <style dangerouslySetInnerHTML={{
                __html: `
    @keyframes slideFromRight {
      from {
        opacity: 0;
        transform: translateX(20px);
      }
      to {
        opacity: 1;
        transform: translateX(0);
      }
    }
    
    @keyframes slideFromLeft {
      from {
        opacity: 0;
        transform: translateX(-20px);
      }
      to {
        opacity: 1;
        transform: translateX(0);
      }
    }
  `
            }} />
        </div>
    );
};

export default TestimonialCarousel;