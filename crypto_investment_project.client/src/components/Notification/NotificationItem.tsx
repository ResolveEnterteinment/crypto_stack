import React from "react";
import INotification from "./INotification";


const NotificationItem: React.FC<{ data: INotification, handleRead: (notificationId: string) => void }> = ({ data, handleRead }) => {

    return (
        <div key={data.id} className={`border-b py-2 ${data.isRead && "text-gray-400"}`}>
            <p>{data.message}</p>
            <small className="text-xs text-gray-500">
                {new Date(data.createdAt).toLocaleString()}
            </small>
            {!data.isRead && (
                <button className="text-blue-500 text-sm ml-2" onClick={() => handleRead(data.id)}>
                    Mark as read
                </button>
            )}
        </div>
    );
};
export default NotificationItem;