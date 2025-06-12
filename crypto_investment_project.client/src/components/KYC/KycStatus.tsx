import React, { useEffect, useState } from 'react';
import { Card, Button, Alert, Spin, Typography } from 'antd';
import { SafetyCertificateOutlined, LoadingOutlined } from '@ant-design/icons';
import kycService from '../../services/kycService';
import { useNavigate } from 'react-router-dom';

const { Title, Text } = Typography;

interface KycStatusProps {
  onStatusChange?: (status: string) => void;
}

const KycStatus: React.FC<KycStatusProps> = ({ onStatusChange }) => {
  const [status, setStatus] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [initiating, setInitiating] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const navigate = useNavigate();

  useEffect(() => {
    fetchKycStatus();
  }, []);

  const fetchKycStatus = async () => {
    try {
      setLoading(true);
      const response = await kycService.getKycStatus();
      if (response.success) {
        setStatus(response.data.status);
        if (onStatusChange) {
          onStatusChange(response.data.status);
        }
      } else {
        setError('Failed to load KYC status');
      }
    } catch (err) {
      setError('An error occurred while fetching KYC status');
      console.error(err);
    } finally {
      setLoading(false);
    }
  };

  const handleInitiateVerification = async () => {
    try {
      setInitiating(true);
      setError(null);
      
      const response = await kycService.initiateSession({
        userId: 'current', // This will be replaced by the backend with the actual user ID
        verificationLevel: 'STANDARD',
        userData: {}
      });
      
      if (response.success && response.sessionId) {
        // Navigate to the verification page with the sessionId
        navigate(`/kyc-verification?sessionId=${response.sessionId}`);
      } else {
        setError(response.message || 'Failed to initiate verification');
      }
    } catch (err) {
      setError('An error occurred while initiating verification');
      console.error(err);
    } finally {
      setInitiating(false);
    }
  };

  // Display loading state
  if (loading) {
    return (
      <Card>
        <Spin indicator={<LoadingOutlined style={{ fontSize: 24 }} spin />} />
        <Text className="mt-3 block">Loading KYC status...</Text>
      </Card>
    );
  }

  // Render appropriate content based on KYC status
  let content;
  switch (status) {
    case 'Approved':
      content = (
        <>
              <SafetyCertificateOutlined style={{ fontSize: 48, color: '#52c41a' }} />
          <Title level={4}>Verification Complete</Title>
          <Text>Your identity has been verified. You now have full access to all platform features.</Text>
        </>
      );
      break;
    case 'Rejected':
      content = (
        <>
          <Alert
            message="Verification Failed"
            description="Your identity verification was unsuccessful. Please contact support for assistance."
            type="error"
            showIcon
          />
          <Button type="primary" onClick={handleInitiateVerification} className="mt-4">
            Try Again
          </Button>
        </>
      );
      break;
    case 'InProgress':
      content = (
        <>
          <Spin />
          <Title level={4}>Verification In Progress</Title>
          <Text>Your verification is currently being processed.</Text>
        </>
      );
      break;
    case 'NeedsReview':
      content = (
        <>
          <Alert
            message="Under Review"
            description="Your verification is currently under review by our team."
            type="warning"
            showIcon
          />
        </>
      );
      break;
    default:
      content = (
        <>
          <Title level={4}>Verification Required</Title>
          <Text className="mb-4 block">
            Please complete identity verification to unlock all platform features.
          </Text>
          <Button 
            type="primary" 
            onClick={handleInitiateVerification}
            loading={initiating}
          >
            Start Verification
          </Button>
        </>
      );
  }

  return (
    <Card title="Identity Verification" className="text-center">
      {error && <Alert message={error} type="error" className="mb-4" />}
      <div className="kyc-status-content">
        {content}
      </div>
    </Card>
  );
};

export default KycStatus;