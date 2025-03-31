import React, { useEffect, useState } from 'react';
import { LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer } from 'recharts';

const PortfolioChart = ({ investmentData, portfolioData }) => {
  const [chartData, setChartData] = useState([]);

  useEffect(() => {
    if (investmentData && portfolioData) {
      // This would normally use real data from your API
      // For demo purposes, creating sample data
      const data = [];
      const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun'];
      
      let investment = 0;
      let investmentStep = investmentData / months.length;
      
      // Sample growth pattern - in real app this would be actual historical data
      const growthFactors = [1, 1.02, 1.05, 0.98, 1.1, 1.15];
      
      months.forEach((month, index) => {
        investment += investmentStep;
        const portfolio = investment * growthFactors[index];
        
        data.push({
          name: month,
          investment: parseFloat(investment.toFixed(2)),
          portfolio: parseFloat(portfolio.toFixed(2))
        });
      });
      
      setChartData(data);
    }
  }, [investmentData, portfolioData]);

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
        <YAxis />
        <Tooltip
          contentStyle={{
            backgroundColor: '#fff',
            border: '1px solid #e5e7eb',
            borderRadius: '8px'
          }}
          formatter={(value) => [`$${value}`, undefined]}
        />
        <Legend />
        <Line
          type="monotone"
          dataKey="investment"
          stroke="#3B82F6"
          strokeWidth={2}
          activeDot={{ r: 8 }}
          name="Investment"
        />
        <Line
          type="monotone"
          dataKey="portfolio"
          stroke="#10B981"
          strokeWidth={2}
          name="Portfolio Value"
        />
      </LineChart>
    </ResponsiveContainer>
  );
};

export default PortfolioChart;