import { Card, Alert, Space, Button } from "antd";
import React from "react";

class ErrorBoundary extends React.Component<
    { children: React.ReactNode },
    { hasError: boolean; error: Error | null }
> {
    constructor(props: { children: React.ReactNode }) {
        super(props);
        this.state = { hasError: false, error: null };
    }

    static getDerivedStateFromError(error: Error) {
        return { hasError: true, error };
    }

    componentDidCatch(error: Error, errorInfo: React.ErrorInfo) {
        console.error('Page Error:', error, errorInfo);
    }

    render() {
        if (this.state.hasError) {
            // TO-DO: Send error to monitoring service here if needed

            return (
                <div style={{
                    minHeight: '100vh',
                    display: 'flex',
                    justifyContent: 'center',
                    alignItems: 'center',
                    padding: '20px'
                }}>
                    <Card style={{ maxWidth: '600px', width: '100%' }}>
                        <Alert
                            message="Something went wrong"
                            description="We encountered an unexpected error. Please refresh the page or contact support if the problem persists."
                            type="error"
                            showIcon
                            action={
                                <Space direction="vertical">
                                    <Button onClick={() => window.location.reload()}>
                                        Refresh Page
                                    </Button>
                                    <Button type="link" onClick={() => window.location.href = '/dashboard'}>
                                        Return to Dashboard
                                    </Button>
                                </Space>
                            }
                        />
                    </Card>
                </div>
            );
        }

        return this.props.children;
    }
}

export default ErrorBoundary;