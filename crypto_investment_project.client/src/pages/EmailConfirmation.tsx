import React, { useEffect, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { Card, Typography, Button, Spin, Result, Space, Alert } from 'antd';
import axios from 'axios';
import api from '../services/api';

const { Paragraph, Text } = Typography;

const EmailConfirmation: React.FC = () => {
  const [status, setStatus] = useState<'loading' | 'success' | 'error'>('loading');
  const [message, setMessage] = useState<string>('');
  const [countdown, setCountdown] = useState<number>(5);
  const location = useLocation();
  const navigate = useNavigate();

  useEffect(() => {
      const confirmEmail = async () => {
          try {
              // Get token from URL query parameter
              const params = new URLSearchParams(location.search);
              const token = params.get('token');

              if (!token) {
                  setStatus('error');
                  setMessage('Invalid confirmation link. Token is missing.');
                  return;
              }

              // Call the API to confirm email
              const response = await api.post('/v1/auth/confirm-email', { token });

              if (response.data.success) {
                  setStatus('success');
                  setMessage('Your email has been confirmed successfully!');

                  // Start countdown for redirect
                  const timer = setInterval(() => {
                      setCountdown((prev) => {
                          if (prev <= 1) {
                              clearInterval(timer);
                              navigate('/login');
                              return 0;
                          }
                          return prev - 1;
                      });
                  }, 1000);

                  return () => clearInterval(timer);
              } else {
                  setStatus('error');
                  setMessage(response.data.message || 'Failed to confirm your email. Please try again.');
              }
          } catch (error) {
              setStatus('error');
              if (axios.isAxiosError(error) && error.response) {
                  setMessage(error.response.data.message || 'Failed to confirm your email. Please try again.');
              } else {
                  setMessage('An unexpected error occurred. Please try again later.');
              }
          }
      };

    confirmEmail();
  }, [location.search, navigate]);

  const renderContent = () => {
    switch (status) {
      case 'loading':
        return (
          <Card style={{ width: 400, textAlign: 'center', boxShadow: '0 4px 12px rgba(0,0,0,0.1)' }}>
            <Space direction="vertical" size="middle" style={{ width: '100%' }}>
              <Spin size="large" />
              <Paragraph>Verifying your email address...</Paragraph>
            </Space>
          </Card>
        );
      
      case 'success':
        return (
          <Card style={{ width: 400, textAlign: 'center', boxShadow: '0 4px 12px rgba(0,0,0,0.1)' }}>
            <Result
              status="success"
              title="Email Verified Successfully!"
              subTitle={message}
              extra={[
                <Alert
                  key="countdown"
                  message={`You will be redirected to the login page in ${countdown} seconds...`}
                  type="info"
                  style={{ marginBottom: 16 }}
                />,
                <Button 
                  type="primary" 
                  key="login" 
                  onClick={() => navigate('/login')}
                  block
                >
                  Go to Login
                </Button>
              ]}
            />
          </Card>
        );
      
      case 'error':
        return (
          <Card style={{ width: 400, textAlign: 'center', boxShadow: '0 4px 12px rgba(0,0,0,0.1)' }}>
            <Result
              status="error"
              title="Email Verification Failed"
              subTitle={message}
              extra={[
                <Space direction="vertical" style={{ width: '100%' }} key="actions">
                  <Button type="primary" onClick={() => navigate('/login')} block>
                    Go to Login
                  </Button>
                  <Button onClick={() => navigate('/register')} block>
                    Register Again
                  </Button>
                  <Button type="link" onClick={() => window.location.reload()}>
                    Try Again
                  </Button>
                </Space>
              ]}
            />
          </Card>
        );
        
      default:
        return null;
    }
  };

  return (
    <div style={{ 
      display: 'flex', 
      justifyContent: 'center', 
      alignItems: 'center', 
      minHeight: '100vh',
      background: '#f5f5f5'
    }}>
      {renderContent()}
    </div>
  );
};

export default EmailConfirmation;