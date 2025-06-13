import ReactDOM from "react-dom/client";
import App from "./App";
import authService from './services/authService';
import "./index.css";

// Initialize auth service (including encryption and CSRF) token BEFORE rendering the app
async function initializeApp() {
    try {
        console.log('Initializing Auth services...');
        // Initialize API services including CSRF protection
        await authService.initialize();
        console.log('Auth services initialized successfully');
    } catch (error) {
        console.warn('Error initializing Auth services:', error);
        // Continue with app initialization even if API services fail
        // The interceptors will handle retry logic for specific requests
    }

    // Only render the app AFTER API initialization
    ReactDOM.createRoot(document.getElementById("root") as HTMLElement).render(
        //<React.StrictMode>
                    <App />
        //</React.StrictMode>
    );
}

// Start the initialization process
initializeApp();