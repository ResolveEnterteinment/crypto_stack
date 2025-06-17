import React, { useEffect, useState } from 'react';
import { CheckCircle, X } from 'lucide-react';

interface PaymentSyncSuccessNotificationProps {
    message: string;
    show: boolean;
    onClose: () => void;
    autoHideDelay?: number;
}

const PaymentSyncSuccessNotification: React.FC<PaymentSyncSuccessNotificationProps> = ({
    message,
    show,
    onClose,
    autoHideDelay = 5000 // 5 seconds default
}) => {
    const [isVisible, setIsVisible] = useState(false);

    useEffect(() => {
        if (show) {
            setIsVisible(true);

            // Auto-hide after delay
            const timer = setTimeout(() => {
                handleClose();
            }, autoHideDelay);

            return () => clearTimeout(timer);
        }
    }, [show, autoHideDelay]);

    const handleClose = () => {
        setIsVisible(false);
        setTimeout(() => {
            onClose();
        }, 300); // Wait for animation to complete
    };

    if (!show) return null;

    return (
        <div className={`fixed top-4 right-4 z-50 transform transition-all duration-300 ease-in-out ${isVisible ? 'translate-x-0 opacity-100' : 'translate-x-full opacity-0'
            }`}>
            <div className="bg-white border border-green-200 rounded-lg shadow-lg p-4 max-w-sm">
                <div className="flex items-start">
                    <div className="flex-shrink-0">
                        <CheckCircle className="h-5 w-5 text-green-400" />
                    </div>
                    <div className="ml-3 flex-1">
                        <p className="text-sm font-medium text-green-800">
                            Payment sync successful
                        </p>
                        <p className="text-sm text-green-700 mt-1">
                            {message}
                        </p>
                    </div>
                    <div className="ml-4 flex-shrink-0">
                        <button
                            onClick={handleClose}
                            className="bg-white rounded-md inline-flex text-green-400 hover:text-green-500 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-green-500"
                        >
                            <span className="sr-only">Close</span>
                            <X className="h-5 w-5" />
                        </button>
                    </div>
                </div>
            </div>
        </div>
    );
};

export default PaymentSyncSuccessNotification;