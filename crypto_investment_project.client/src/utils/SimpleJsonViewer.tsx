// SimpleJsonViewer.tsx
import { Tag } from 'antd';
import { useState } from 'react';

type SimpleJsonViewerProps = {
    data: any;
    level?: number;
    collapsedLevels?: number;
};

export function SimpleJsonViewer({ data, level = 0, collapsedLevels = 1 }: SimpleJsonViewerProps) {
    const [collapsed, setCollapsed] = useState(level < collapsedLevels);

    if (data === null) return <span style={{ color: '#a31515' }}>null</span>;
    if (typeof data === 'boolean') return <span style={{ color: '#1c00cf' }}>{data.toString()}</span>;
    if (typeof data === 'number') return <span style={{ color: '#098658' }}>{data}</span>;
    if (typeof data === 'string') return <span style={{ color: '#a31515' }}>"{data}"</span>;

    if (Array.isArray(data)) {
        if (data.length === 0) return <span>Empty</span>;
        return (
            <div style={{ marginLeft: 16 }}>
                <span
                    style={{ cursor: 'pointer', color: '#888' }}
                >
                    {collapsed ? (
                        <span
                            style={{ cursor: 'pointer', color: '#888' }}
                            onClick={() => setCollapsed(!collapsed)}
                        >
                            Array
                        </span>
                    ) : (
                        <div>
                        {data.map((item, idx) => (
                            <div key={idx}>
                                <SimpleJsonViewer data={item} level={level + 1} collapsedLevels={collapsedLevels} />
                            </div>
                        ))}
                        </div>
                    )}
                </span>
            </div>
        );
    }

    if (typeof data === 'object') {
        const keys = Object.keys(data);
        if (keys.length === 0) return <span>{'Empty'}</span>;
        return (
            <div style={{ marginLeft: 16 }}>
                <span
                    style={{ cursor: 'pointer', color: '#888' }}
                >
                    {Object.entries(data).map(([key, value]) => (
                        <div key={key}>
                            <Tag style={{ color: '#0451a5' }} onClick={() => setCollapsed(!collapsed)} >{key}</Tag>:
                            {collapsed ? (
                                <span
                                    style={{ cursor: 'pointer', color: '#888' }}
                                    onClick={() => setCollapsed(!collapsed)}
                                >
                                    Object
                                </span>
                            ) : (
                                <span>
                                        <SimpleJsonViewer data={value} level={level + 1} collapsedLevels={collapsedLevels} />
                                </span>
                            )}
                        </div>
                    ))}
                </span>
            </div>
        );
    }

    return <span />;
}