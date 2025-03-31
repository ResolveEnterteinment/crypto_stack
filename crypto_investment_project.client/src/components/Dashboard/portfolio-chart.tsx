// src/components/Dashboard/portfolio-chart.tsx
import React, { useEffect, useState } from 'react';
import { LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer } from 'recharts';

interface PortfolioChartProps {
    investmentData: number;
    portfolioData: number;
}

interface ChartDataPoint {
    name: string;
    investment: number;
    portfolio: number;
}

const PortfolioChart: React.FC<PortfolioChartProps> = ({ investmentData, portfolioData }) => {
    const [chartData, setChartData] = useState<ChartDataPoint[]>([]);

    useEffect(() => {
        if (investmentData > 0 || portfolioData > 0) {
            // Create time-series data based on current values
            // In a real implementation, you would fetch historical data from your API
            generateSampleChartData();
        }
    }, [investmentData, portfolioData]);

    const generateSampleChartData = () => {
        const data: ChartDataPoint[] = [];
        const now = new Date();

        // Generate data for the last 6 months
        for (let i = 5; i >= 0; i--) {
            const date = new Date(now);
            date.setMonth(now.getMonth() - i);

            const monthName = date.toLocaleString('default', { month: 'short' });

            // Create a realistic growth pattern
            // Investment grows linearly
            const monthlyInvestment = investmentData / 6;
            const currentInvestment = monthlyInvestment * (6 - i);

            // Portfolio value has more variance
            // This simulates market fluctuations
            const growthFactors = [0.98, 1.02, 0.99, 1.05, 1.03, 1.01];
            const randomFactor = 0.95 + (Math.random() * 0.1); // Random factor between 0.95 and 1.05

            // This creates a more realistic portfolio value that generally tracks with investments
            // but has some variance
            const portfolioValue = currentInvestment * growthFactors[5 - i] * randomFactor;

            data.push({
                name: monthName,
                investment: parseFloat(currentInvestment.toFixed(2)),
                portfolio: parseFloat(portfolioValue.toFixed(2))
            });
        }

        // Ensure the final point matches exactly with the current data
        if (data.length > 0) {
            data[data.length - 1].investment = parseFloat(investmentData.toFixed(2));
            data[data.length - 1].portfolio = parseFloat(portfolioData.toFixed(2));
        }

        setChartData(data);
    };

    // Early return if no data
    if (!chartData.length) return (
        <div className="h-64 flex items-center justify-center text-gray-400">
            Loading chart data...
        </div>
    );

    return (
        <ResponsiveContainer width="100%" height={320}>
            <LineChart
                data={chartData}
                margin={{
                    top: 5,
                    right: 30,
                    left: 20,
                    bottom: 5,
                }}
            >
                <CartesianGrid strokeDasharray="3 3" stroke="#e5e7eb" />
                <XAxis dataKey="name" />
                <YAxis
                    tickFormatter={(value) => `$${value}`}
                />
                <Tooltip
                    contentStyle={{
                        backgroundColor: '#fff',
                        border: '1px solid #e5e7eb',
                        borderRadius: '8px'
                    }}
                    formatter={(value: number) => [`$${value.toFixed(2)}`, undefined]}
                    labelFormatter={(label) => `Month: ${label}`}
                />
                <Legend />
                <Line
                    type="monotone"
                    dataKey="investment"
                    stroke="#3B82F6"
                    strokeWidth={2}
                    activeDot={{ r: 8 }}
                    name="Investment"
                    dot={{ strokeWidth: 2 }}
                />
                <Line
                    type="monotone"
                    dataKey="portfolio"
                    stroke="#10B981"
                    strokeWidth={2}
                    name="Portfolio Value"
                    dot={{ strokeWidth: 2 }}
                />
            </LineChart>
        </ResponsiveContainer>
    );
};

export default PortfolioChart;