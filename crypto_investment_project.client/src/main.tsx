// src/index.tsx
import React from "react";
import ReactDOM from "react-dom/client";
import "./index.css";
import App from "./App";

const root = ReactDOM.createRoot(document.getElementById("root") as HTMLElement);
root.render(
    <React.StrictMode>
        <div className="bg-blue-500 text-white p-4">Hello, World!</div>
        <App />
    </React.StrictMode>
);