import React, { useState } from 'react';
import Notifications from '../components/Notifications';

const DashboardPage: React.FC = () => {
    const [isModalOpen, setModalOpen] = useState(false);
    const [showNotifications, setShowNotifications] = useState(false);
    const userId = 'user-id-placeholder';

  return (
    <div className="min-h-screen bg-gray-100 py-8 px-4 lg:px-10">
          <div className="flex justify-between items-center mb-6">
              <h1 className="text-3xl font-bold">Dashboard</h1>
              <button className="relative" onClick={() => setShowNotifications(!showNotifications)}>
                  <span className="text-xl">🔔</span>
                  <span className="absolute top-0 right-0 inline-block w-3 h-3 bg-red-600 rounded-full"></span>
              </button>
          </div>

          {showNotifications && <Notifications userId={userId} />}

      {/* Total Balances Section */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-6 mb-8">
        <div className="bg-white shadow rounded-lg p-4">
          <h2 className="text-xl font-semibold">Total Crpto Balances</h2>
          <p className="text-2xl font-bold mt-2">$25,000.00 USD</p>
        </div>

        <div className="bg-white shadow rounded-lg p-4">
          <h2 className="text-xl font-semibold">Total USD Investment</h2>
          <p className="text-2xl font-bold mt-2">$20,000.00 USD</p>
        </div>

        <div className="bg-white shadow rounded-lg p-4">
          <h2 className="text-xl font-semibold">Portfolio Value</h2>
          <p className="text-2xl font-bold mt-2">$30,000.00 USD</p>
        </div>
      </div>

      {/* Subscriptions Section */}
      <div className="mb-8">
        <h2 className="text-2xl font-bold mb-4">Your Subscriptions</h2>
        <div className="grid grid-cols-1 lg:grid-cols-2 xl:grid-cols-3 gap-6">
          {[1, 2, 3].map((sub) => (
            <div key={sub} className="bg-white shadow-lg rounded-xl p-6 relative">
              <div className="mb-4">
                <h3 className="text-xl font-semibold">Subscription #{sub}</h3>
                <p className="text-gray-600">Started: Jan 10, 2025</p>
              </div>
              <div className="mb-4">
                <p className="text-sm font-semibold">Interval: <span className="font-normal">Monthly</span></p>
                <p className="text-sm font-semibold">Next Due Date: <span className="font-normal">Feb 10, 2025</span></p>
                <p className="text-sm font-semibold">Total Payments: <span className="font-normal">12</span></p>
                <p className="text-sm font-semibold">Status: <span className="text-green-500 font-normal">Active</span></p>
              </div>

              <div className="mb-4">
                <h4 className="font-semibold">Allocations:</h4>
                <ul className="list-disc list-inside text-sm">
                  <li>BTC: 50%</li>
                  <li>ETH: 30%</li>
                  <li>LINK: 20%</li>
                </ul>
              </div>

              <div className="flex gap-2 absolute bottom-4 right-4">
                <button className="bg-blue-500 text-white px-3 py-1 rounded-md text-sm hover:bg-blue-600">Edit</button>
                <button className="bg-red-500 text-white px-3 py-1 rounded-md text-sm hover:bg-red-600">Cancel</button>
                <button onClick={() => setModalOpen(true)} className="bg-gray-500 text-white px-3 py-1 rounded-md text-sm hover:bg-gray-600">History</button>
              </div>
            </div>
          ))}
        </div>
      </div>

      {/* Chart Placeholder */}
      <div className="bg-white shadow rounded-lg p-4">
        <h2 className="text-2xl font-bold mb-4">Investment vs Portfolio (USD)</h2>
        <div className="h-64 flex items-center justify-center text-gray-400">
          [Chart Placeholder]
        </div>
      </div>

      {/* Transactions Modal */}
      {isModalOpen && (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex justify-center items-center">
          <div className="bg-white p-6 rounded-xl shadow-lg max-w-xl w-full">
            <h3 className="text-xl font-bold mb-4">Transaction History</h3>
            <ul className="mb-4 max-h-72 overflow-auto">
              <li className="border-b py-2">Jan 10, 2025 - BTC Buy - $500</li>
              <li className="border-b py-2">Feb 10, 2025 - ETH Buy - $300</li>
            </ul>
            <button
              className="bg-blue-600 text-white py-2 px-4 rounded-md hover:bg-blue-700"
              onClick={() => setModalOpen(false)}>
              Close
            </button>
          </div>
        </div>
      )}
    </div>
  );
};

export default DashboardPage;
