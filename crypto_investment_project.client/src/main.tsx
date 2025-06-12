import ReactDOM from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import App from "./App";
import api from './services/api';
import { AuthProvider } from "./context/AuthContext";
import { NotificationProvider } from "./context/NotificationContext";
import "./index.css";

// Initialize API service including CSRF token BEFORE rendering the app
async function initializeApp() {
    try {
        console.log('Initializing API services...');
        // Initialize API services including CSRF protection
        await api.initialize();
        console.log('API services initialized successfully');
    } catch (error) {
        console.warn('Error initializing API services:', error);
        // Continue with app initialization even if API services fail
        // The interceptors will handle retry logic for specific requests
    }

    // Only render the app AFTER API initialization
    ReactDOM.createRoot(document.getElementById("root") as HTMLElement).render(
        //<React.StrictMode>
        <AuthProvider>
            <NotificationProvider>
                <BrowserRouter>
                    <App />
                </BrowserRouter>
            </NotificationProvider>
        </AuthProvider>
        //</React.StrictMode>
    );
}

// Start the initialization process
initializeApp();