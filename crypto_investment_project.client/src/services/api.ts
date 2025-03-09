// src/services/api.ts
import axios from "axios";

const api = axios.create({
    baseURL: "http://localhost:7144/api/v1/authenticate", // Adjust based on your back-end port
    headers: {
        "Content-Type": "application/json",
    },
});

api.interceptors.request.use((config) => {
    const token = localStorage.getItem("token");
    if (token) {
        config.headers.Authorization = `Bearer ${token}`;
    }
    return config;
});

export default api;