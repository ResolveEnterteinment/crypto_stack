import axios from "axios";

const api = axios.create({
    baseURL: import.meta.env.VITE_API_BASE_URL || "https://localhost:7144/api",
    headers: {
        "Content-Type": "application/json",
    },
});

// Attach Authorization token to every request
api.interceptors.request.use((config) => {
    const token = localStorage.getItem("access_token");
    if (token) {
        config.headers.Authorization = `Bearer ${token}`;
    }
    return config;
});

// Add response interceptor for better error handling
api.interceptors.response.use(
    response => response,
    error => {
        // Log all API errors
        console.error("API Error:",
            error.response?.status,
            error.response?.data || error.message);

        return Promise.reject(error);
    }
);

export default api;
